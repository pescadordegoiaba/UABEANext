using AssetsTools.NET;
using AssetsTools.NET.Cpp2IL;
using AssetsTools.NET.Extra;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using UABEANext4.Logic.Configuration;
using UABEANext4.Logic.Il2Cpp;
using UABEANext4.Plugins;
using UABEANext4.Util;

namespace UABEANext4.AssetWorkspace;

public partial class Workspace : ObservableObject
{
    public AssetsManager Manager { get; } = new AssetsManager();
    public PluginLoader Plugins { get; } = new PluginLoader();
    public AssetNamer Namer { get; }

    public Mutex ModifyMutex { get; } = new Mutex();

    // this should be its own class
    [ObservableProperty]
    public float _progressValue = 0f;
    [ObservableProperty]
    public string _progressText = "";

    public ObservableCollection<WorkspaceItem> RootItems { get; } = new();
    public Dictionary<string, WorkspaceItem> ItemLookup { get; } = new();
    private SynchronizationContext? FileSyncContext { get; } = SynchronizationContext.Current;

    // items modified and unsaved
    public HashSet<WorkspaceItem> UnsavedItems { get; } = new();
    // items modified and saved
    // we track this since the base AssetsFile is still reading from the old file
    public HashSet<WorkspaceItem> ModifiedItems { get; } = new();

    public int NextLoadIndex => RootItems.Count != 0 ? RootItems.Max(i => i.LoadIndex) + 1 : 0;

    public delegate void MonoTemplateFailureEvent(string path);
    public event MonoTemplateFailureEvent? MonoTemplateLoadFailed;

    private bool _setMonoTempGeneratorsYet;

    public void ResetMonoTemplateGenerators()
    {
        _setMonoTempGeneratorsYet = false;
        Manager.MonoTempGenerator = null;
        VerboseLog.Log("Workspace", "Reset mono/IL2CPP template generators");
    }

    /// <summary>
    /// Detects APK/game folder layout and registers MonoCecil when the project uses Unity Mono.
    /// </summary>
    public bool TryRegisterMonoFromGameRoot(string root)
    {
        var probe = Il2CppProjectProbe.Probe(root);
        if (probe.Backend != UnityScriptBackend.Mono || string.IsNullOrEmpty(probe.ManagedDirectory))
        {
            VerboseLog.Log("Workspace", $"TryRegisterMonoFromGameRoot failed: {probe.Summary}");
            return false;
        }

        return TryRegisterMonoFromManagedDirectory(probe.ManagedDirectory);
    }

    public bool TryRegisterMonoFromManagedDirectory(string managedDirectory)
    {
        if (string.IsNullOrWhiteSpace(managedDirectory) || !Directory.Exists(managedDirectory))
        {
            VerboseLog.Log("Workspace", $"TryRegisterMonoFromManagedDirectory invalid path: {managedDirectory}");
            return false;
        }

        ResetMonoTemplateGenerators();
        Manager.MonoTempGenerator = new MonoCecilTempGenerator(managedDirectory);
        _setMonoTempGeneratorsYet = true;
        VerboseLog.Log("Workspace", $"Registered MonoCecil from {managedDirectory}");
        return true;
    }

    /// <summary>
    /// Installs decrypted metadata from Il2CppDumper and refreshes Cpp2IL template generator.
    /// </summary>
    public bool ApplyIl2CppDumpResult(string metadataPath, string il2CppBinaryPath, string? gameDataDirectory = null)
    {
        if (!File.Exists(metadataPath) || !File.Exists(il2CppBinaryPath))
        {
            VerboseLog.Log("Workspace", $"ApplyIl2CppDumpResult missing files meta={metadataPath} asm={il2CppBinaryPath}");
            return false;
        }

        gameDataDirectory ??= Path.GetDirectoryName(metadataPath);
        if (string.IsNullOrEmpty(gameDataDirectory))
        {
            return false;
        }

        var targetMetaDir = Path.Combine(gameDataDirectory, "Metadata");
        if (!Directory.Exists(targetMetaDir))
        {
            targetMetaDir = gameDataDirectory;
        }

        var targetMeta = Path.Combine(targetMetaDir, "global-metadata.dat");
        var uabeaMeta = Path.Combine(targetMetaDir, "global-metadata-uabea.dat");

        try
        {
            Directory.CreateDirectory(targetMetaDir);
            File.Copy(metadataPath, uabeaMeta, overwrite: true);
            if (File.Exists(targetMeta))
            {
                var backup = targetMeta + ".protected.bak";
                if (!File.Exists(backup))
                {
                    File.Copy(targetMeta, backup, overwrite: false);
                }
            }

            File.Copy(metadataPath, targetMeta, overwrite: true);
            VerboseLog.Log("Workspace", $"Installed metadata to {targetMeta} (backup/uabea in same folder)");
        }
        catch (Exception ex)
        {
            VerboseLog.LogException("Workspace", ex, "ApplyIl2CppDumpResult copy failed");
            return false;
        }

        ResetMonoTemplateGenerators();
        Manager.MonoTempGenerator = new Cpp2IlTempGenerator(uabeaMeta, il2CppBinaryPath);
        VerboseLog.Log("Workspace", $"Cpp2IL generator: meta={uabeaMeta}, asm={il2CppBinaryPath}");
        return true;
    }
    private string? _loadedClassDatabaseVersion;
    public string? LastBaseFieldReadError { get; private set; }

    public Workspace()
    {
        using var scope = VerboseLog.Scope("Workspace", "ctor");
        string classDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "classdata.tpk");
        VerboseLog.Log("Workspace", $"BaseDirectory={AppDomain.CurrentDomain.BaseDirectory}, SyncContext={FileSyncContext?.GetType().Name ?? "null"}");
        if (!File.Exists(classDataPath))
        {
            VerboseLog.Log("Workspace", $"Missing classdata.tpk at {classDataPath}");
            throw new FileNotFoundException(
                "The required Unity class database package classdata.tpk is missing from the application directory.",
                classDataPath);
        }

        Manager.LoadClassPackage(classDataPath);
        VerboseLog.Log("Workspace", $"Loaded class package from {classDataPath}");

        string pluginsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");
        Plugins.LoadPluginsInDirectory(pluginsPath);

        Manager.UseRefTypeManagerCache = true;
        // Template shapes can differ between Unity versions and custom type trees.
        // Avoid a global type-id cache so the inspector always reads the current file's layout.
        Manager.UseTemplateFieldCache = false;
        Manager.UseQuickLookup = true;

        Namer = new AssetNamer(this);
        scope.Complete($"PluginsPath={pluginsPath}");
    }

    public WorkspaceItem? LoadAnyFile(Stream stream, int loadOrder = -1, string path = "")
    {
        using var scope = VerboseLog.Scope("Workspace", "LoadAnyFile", $"path={path}, loadOrder={loadOrder}");
        if (path == "" && stream is FileStream fs)
        {
            path = fs.Name;
        }

        var detectedType = FileTypeDetector.DetectFileType(new AssetsFileReader(stream), 0);
        VerboseLog.Log("Workspace", $"Detected type={detectedType} for {path}");
        if (detectedType == DetectedFileType.BundleFile)
        {
            stream.Position = 0;
            return LoadBundle(stream, loadOrder);
        }
        else if (detectedType == DetectedFileType.AssetsFile)
        {
            stream.Position = 0;
            return LoadAssets(stream, loadOrder);
        }
        else if (path.EndsWith(".resS") || path.EndsWith(".resource"))
        {
            return LoadResource(stream, loadOrder);
        }

        VerboseLog.Log("Workspace", $"Unsupported file type for {path}");
        return null;
    }

    public WorkspaceItem LoadBundle(Stream stream, int loadOrder = -1, string name = "")
    {
        using var scope = VerboseLog.Scope("Workspace", "LoadBundle", $"name={name}, loadOrder={loadOrder}");
        // todo: don't always unpack to memory lol
        BundleFileInstance bunInst;
        if (stream is FileStream fs)
        {
            bunInst = Manager.LoadBundleFile(fs);
        }
        else
        {
            bunInst = Manager.LoadBundleFile(stream, name);
        }

        TryLoadClassDatabase(bunInst.file);
        VerboseLog.Log("Workspace",
            $"Bundle loaded: {bunInst.name}, engine={bunInst.file.Header.EngineVersion}, " +
            $"compression={bunInst.originalCompression}, blockDirAtEnd={bunInst.originalBlockAndDirAtEnd}, " +
            "children will be created");

        var item = new WorkspaceItem(this, bunInst, loadOrder);
        AddRootItemThreadSafe(item, bunInst.name);

        scope.Complete($"bundle={bunInst.name}, childCount={item.Children.Count}");
        return item;
    }

    public WorkspaceItem LoadAssets(Stream stream, int loadOrder = -1, string name = "")
    {
        using var scope = VerboseLog.Scope("Workspace", "LoadAssets", $"name={name}, loadOrder={loadOrder}");
        AssetsFileInstance fileInst;
        if (stream is FileStream fs)
        {
            fileInst = Manager.LoadAssetsFile(fs);
        }
        else
        {
            fileInst = Manager.LoadAssetsFile(stream, name);
        }

        TryLoadClassDatabase(fileInst.file);

        FixupAssetsFile(fileInst);

        var item = new WorkspaceItem(fileInst, loadOrder);
        AddRootItemThreadSafe(item, fileInst.name);

        if (fileInst.file.Header.Version < 10 && fileInst.file.AssetInfos.Count == 0)
        {
            VerboseLog.Log("Workspace",
                $"Warning: {fileInst.name} uses legacy format v{fileInst.file.Header.Version} but reports 0 assets after read. " +
                "If this persists after updating, the file may be empty or use an unsupported layout.");
        }

        scope.Complete($"file={fileInst.name}, unity={fileInst.file.Metadata.UnityVersion}, hdr={fileInst.file.Header.Version}, assets={fileInst.file.AssetInfos.Count}, typeTree={fileInst.file.Metadata.TypeTreeEnabled}");
        return item;
    }

    public WorkspaceItem LoadAssetsFromBundle(BundleFileInstance bunInst, int index)
    {
        using var scope = VerboseLog.Scope("Workspace", "LoadAssetsFromBundle", $"bundle={bunInst.name}, index={index}");
        var dirInf = BundleHelper.GetDirInfo(bunInst.file, index);
        var fileInst = Manager.LoadAssetsFileFromBundle(bunInst, index);

        TryLoadClassDatabase(fileInst.file);

        FixupAssetsFile(fileInst);

        var item = new WorkspaceItem(dirInf.Name, fileInst, -1, WorkspaceItemType.AssetsFile);
        scope.Complete($"inner={dirInf.Name}, assets={fileInst.file.AssetInfos.Count}");
        return item;
    }

    private void FixupAssetsFile(AssetsFileInstance fileInst)
    {
        if (fileInst.file.AssetInfos is not RangeObservableCollection<AssetFileInfo>)
        {
            VerboseLog.Log("Workspace", $"FixupAssetsFile converting AssetInfos to AssetInst for {fileInst.name}");
            var assetInsts = new RangeObservableCollection<AssetFileInfo>();
            var tmp = new List<AssetFileInfo>();
            var maxNameLen = ConfigurationManager.Settings.ListingNameLength;
            foreach (var info in fileInst.file.AssetInfos)
            {
                var asset = new AssetInst(fileInst, info);
                asset.AssetName = Namer.GetAssetName(asset, true, maxNameLen);

                tmp.Add(asset);
            }
            assetInsts.AddRange(tmp);
            fileInst.file.Metadata.AssetInfos = assetInsts;
            fileInst.file.GenerateQuickLookup();
            VerboseLog.Log("Workspace", $"FixupAssetsFile done: {tmp.Count} assets wrapped");
        }
        else
        {
            VerboseLog.Log("Workspace", $"FixupAssetsFile skipped (already AssetInst collection) for {fileInst.name}");
        }
    }

    public void TryLoadClassDatabase(AssetBundleFile file)
    {
        var fileVersion = file.Header.EngineVersion;
        if (fileVersion != "0.0.0")
        {
            EnsureClassDatabase(fileVersion);
        }
    }

    public void TryLoadClassDatabase(AssetsFile file)
    {
        var metadata = file.Metadata;
        var fileVersion = metadata.UnityVersion;
        if (fileVersion != "0.0.0")
        {
            EnsureClassDatabase(fileVersion);
        }
    }

    public WorkspaceItem LoadResource(Stream stream, int loadOrder = -1, string name = "")
    {
        if (name == "" && stream is FileStream fs)
        {
            name = Path.GetFileName(fs.Name);
        }

        WorkspaceItem item = new WorkspaceItem(name, stream, loadOrder, WorkspaceItemType.ResourceFile);
        AddRootItemThreadSafe(item, name);

        return item;
    }

    internal void AddRootItemThreadSafe(WorkspaceItem item, string itemName)
    {
        FileSyncContext?.Post(_ =>
        {
            if (item.LoadIndex != -1)
            {
                int pos = RootItems.BinarySearch(item, (i, j) => i.LoadIndex.CompareTo(j.LoadIndex));
                if (pos < 0)
                {
                    RootItems.Insert(~pos, item);
                }
                else
                {
                    RootItems.Insert(pos, item);
                }
                ItemLookup[itemName] = item;
                return;
            }

            RootItems.Add(item);
            ItemLookup[itemName] = item;
        }, null);
    }

    internal void AddChildItemThreadSafe(WorkspaceItem item, WorkspaceItem parent, string itemName)
    {
        FileSyncContext?.Post(_ =>
        {
            // loadorder ignored here
            parent.Children.Add(item);
            item.Parent = parent;
            ItemLookup[itemName] = item;
        }, null);
    }

    public void SetProgressThreadSafe(float value, string text)
    {
        var roundedValue = (float)Math.Round(value * 20) / 20;
        if (Math.Abs(roundedValue - ProgressValue) >= 0.05f || value == 0f || value == 1f)
        {
            FileSyncContext?.Post(_ =>
            {
                ProgressValue = value;
                ProgressText = text;
            }, null);
        }
    }

    // should be nullable
    public AssetTypeTemplateField GetTemplateField(AssetInst asset, bool skipMonoBehaviourFields = false)
    {
        EnsureClassDatabase(asset.FileInstance);

        AssetReadFlags readFlags = AssetReadFlags.None;
        if (skipMonoBehaviourFields && asset.Type == AssetClassID.MonoBehaviour)
        {
            readFlags |= AssetReadFlags.SkipMonoBehaviourFields | AssetReadFlags.ForceFromCldb;
        }

        return Manager.GetTemplateBaseField(asset.FileInstance, asset, readFlags);
    }

    public AssetTypeTemplateField GetTemplateField(AssetsFileInstance fileInst, AssetFileInfo info, bool skipMonoBehaviourFields = false)
    {
        EnsureClassDatabase(fileInst);

        AssetReadFlags readFlags = AssetReadFlags.None;
        if (skipMonoBehaviourFields && info.TypeId == (int)AssetClassID.MonoBehaviour)
        {
            readFlags |= AssetReadFlags.SkipMonoBehaviourFields | AssetReadFlags.ForceFromCldb;
        }

        return Manager.GetTemplateBaseField(fileInst, info, readFlags);
    }

    public void CheckAndSetMonoTempGenerators(AssetsFileInstance fileInst, AssetFileInfo? info)
    {
        bool isValidMono = info == null || info.TypeId == (int)AssetClassID.MonoBehaviour || info.TypeId < 0;
        if (isValidMono && !_setMonoTempGeneratorsYet && !fileInst.file.Metadata.TypeTreeEnabled)
        {
            string dataDir = PathUtils.GetAssetsFileDirectory(fileInst);
            bool success = SetMonoTempGenerators(dataDir);
            if (!success)
            {
                MonoTemplateLoadFailed?.Invoke(dataDir);
            }
        }
    }

    private bool SetMonoTempGenerators(string fileDir)
    {
        if (!_setMonoTempGeneratorsYet)
        {
            _setMonoTempGeneratorsYet = true;

            string managedDir = Path.Combine(fileDir, "Managed");
            FindCpp2IlFilesResult il2cppFiles = FindCpp2IlFiles.Find(fileDir);

            bool managedExists = Directory.Exists(managedDir);
            bool il2cppExists = il2cppFiles.success;
            VerboseLog.Log("Workspace", $"SetMonoTempGenerators dir={fileDir}, managed={managedExists}, il2cpp={il2cppExists}");

            if (managedExists && (!il2cppExists || ConfigurationManager.Settings.UseManagedOverIl2cpp))
            {
                bool hasDll = Directory.GetFiles(managedDir, "*.dll").Length > 0;
                if (hasDll)
                {
                    Manager.MonoTempGenerator = new MonoCecilTempGenerator(managedDir);
                    VerboseLog.Log("Workspace", $"Using MonoCecilTempGenerator at {managedDir}");
                    return true;
                }
            }

            if (il2cppExists)
            {
                Manager.MonoTempGenerator = new Cpp2IlTempGenerator(il2cppFiles.metaPath, il2cppFiles.asmPath);
                VerboseLog.Log("Workspace", $"Using Cpp2IlTempGenerator meta={il2cppFiles.metaPath}, asm={il2cppFiles.asmPath}");
                return true;
            }

            VerboseLog.Log("Workspace", "No Mono/IL2CPP generator available for this directory");
        }
        return false;
    }

    public AssetFileInfo? GetAssetFileInfo(AssetsFileInstance fileInst, AssetTypeValueField pptrField)
    {
        return GetAssetFileInfo(fileInst, pptrField["m_FileID"].AsInt, pptrField["m_PathID"].AsLong);
    }

    public AssetFileInfo? GetAssetFileInfo(AssetsFileInstance fileInst, int fileId, long pathId)
    {
        if (fileId != 0)
        {
            fileInst = fileInst.GetDependency(Manager, fileId - 1);
        }
        if (fileInst == null)
        {
            return null;
        }

        return fileInst.file.GetAssetInfo(pathId);
    }

    public AssetInst? GetAssetInst(AssetsFileInstance fileInst, AssetTypeValueField pptrField)
    {
        return GetAssetInst(fileInst, pptrField["m_FileID"].AsInt, pptrField["m_PathID"].AsLong);
    }

    public AssetInst? GetAssetInst(AssetsFileInstance fileInst, int fileId, long pathId)
    {
        if (fileId != 0)
        {
            fileInst = fileInst.GetDependency(Manager, fileId - 1);
            fileId = 0;
        }
        AssetFileInfo? info = GetAssetFileInfo(fileInst, fileId, pathId);

        if (info == null)
        {
            return null;
        }
        else if (info is AssetInst inst)
        {
            return inst;
        }
        else
        {
            return new AssetInst(fileInst, info);
        }
    }

    public AssetTypeValueField? GetBaseField(AssetInst asset)
    {
        return GetBaseField(asset.FileInstance, asset.PathId);
    }

    public AssetTypeValueField? GetBaseField(AssetsFileInstance fileInst, long pathId)
    {
        return GetBaseField(fileInst, 0, pathId);
    }

    public AssetTypeValueField? GetBaseField(AssetsFileInstance fileInst, AssetTypeValueField pptrField)
    {
        return GetBaseField(fileInst, pptrField["m_FileID"].AsInt, pptrField["m_PathID"].AsLong);
    }

    public AssetTypeValueField? GetBaseField(AssetsFileInstance fileInst, int fileId, long pathId)
    {
        using var scope = VerboseLog.Scope("Workspace", "GetBaseField", $"file={fileInst?.name}, fileId={fileId}, pathId={pathId}");
        LastBaseFieldReadError = null;

        if (fileId != 0)
        {
            fileInst = fileInst.GetDependency(Manager, fileId - 1);
            VerboseLog.Log("Workspace", $"Resolved dependency fileId={fileId} -> {fileInst?.name ?? "null"}");
        }
        if (fileInst == null)
        {
            LastBaseFieldReadError = "Dependency assets file not loaded.";
            VerboseLog.Log("Workspace", LastBaseFieldReadError);
            return null;
        }

        AssetFileInfo? info = fileInst.file.GetAssetInfo(pathId);
        if (info == null)
        {
            LastBaseFieldReadError = $"Asset pathId {pathId} not found in {fileInst.name}.";
            VerboseLog.Log("Workspace", LastBaseFieldReadError);
            return null;
        }

        CheckAndSetMonoTempGenerators(fileInst, info);
        EnsureClassDatabase(fileInst);

        // negative target platform seems to indicate an editor version
        AssetReadFlags readFlags = AssetReadFlags.None;
        if ((int)fileInst.file.Metadata.TargetPlatform < 0)
        {
            readFlags |= AssetReadFlags.PreferEditor;
        }

        VerboseLog.Log("Workspace",
            $"Reading typeId={info.TypeId}, scriptIndex={info.ScriptTypeIndex}, byteSize={info.ByteSize}, " +
            $"unity={fileInst.file.Metadata.UnityVersion}, typeTree={fileInst.file.Metadata.TypeTreeEnabled}, " +
            $"cldb={Manager.ClassDatabase?.Header.Version}, monoGen={Manager.MonoTempGenerator?.GetType().Name ?? "null"}, flags={readFlags}");

        try
        {
            var baseField = Manager.GetBaseField(fileInst, info, readFlags);
            if (baseField is null && fileInst.file.Metadata.TypeTreeEnabled && Manager.ClassDatabase is not null)
            {
                readFlags |= AssetReadFlags.ForceFromCldb;
                VerboseLog.Log("Workspace", $"Retrying GetBaseField with ForceFromCldb for pathId={pathId}");
                baseField = Manager.GetBaseField(fileInst, info, readFlags);
            }

            if (baseField is null)
            {
                LastBaseFieldReadError = $"No template found for type {info.TypeId} in Unity {fileInst.file.Metadata.UnityVersion}.";
                scope.Fail(LastBaseFieldReadError);
            }
            else
            {
                scope.Complete($"ok type={baseField.TypeName}, children={baseField.Children.Count}");
            }

            return baseField;
        }
        catch (ObjectDisposedException ex)
        {
            scope.Fail(ex);
            throw;
        }
        catch (Exception ex)
        {
            LastBaseFieldReadError = ex.Message;
            scope.Fail(ex);
            return null;
        }
    }

    private void EnsureClassDatabase(AssetsFileInstance fileInst)
    {
        EnsureClassDatabase(fileInst.file.Metadata.UnityVersion);
    }

    private void EnsureClassDatabase(string fileVersion)
    {
        if (string.IsNullOrWhiteSpace(fileVersion) || fileVersion == "0.0.0")
        {
            VerboseLog.Log("Workspace", $"EnsureClassDatabase skipped for version '{fileVersion}'");
            return;
        }

        if (Manager.ClassDatabase is not null && _loadedClassDatabaseVersion == fileVersion)
        {
            return;
        }

        if (_loadedClassDatabaseVersion is not null && _loadedClassDatabaseVersion != fileVersion)
        {
            VerboseLog.Log("Workspace", $"Switching class database {_loadedClassDatabaseVersion} -> {fileVersion}");
            Manager.UnloadClassDatabase();
            Manager.MonoTempGenerator?.Dispose();
            Manager.MonoTempGenerator = null;
            _setMonoTempGeneratorsYet = false;
        }

        Manager.LoadClassDatabaseFromPackage(fileVersion);
        _loadedClassDatabaseVersion = fileVersion;
        VerboseLog.Log("Workspace", $"Loaded class database for Unity {fileVersion}");
    }

    public void Dirty(WorkspaceItem item)
    {
        UnsavedItems.Add(item);
        ModifiedItems.Add(item);
        if (item.Parent != null)
        {
            Dirty(item.Parent);
        }
    }

    public void Close(WorkspaceItem item)
    {
        if (!item.Loaded || !RootItems.Contains(item))
            return;

        var itemObj = item.Object;
        if (item.ObjectType == WorkspaceItemType.ResourceFile)
        {
            var stream = (Stream)itemObj;
            stream.Close();
        }
        else if (item.ObjectType == WorkspaceItemType.BundleFile)
        {
            var bunInst = (BundleFileInstance)itemObj;
            Manager.UnloadBundleFile(bunInst);
        }
        else if (item.ObjectType == WorkspaceItemType.AssetsFile && item.Parent is null)
        {
            var fileInst = (AssetsFileInstance)itemObj;
            Manager.UnloadAssetsFile(fileInst);
        }

        RootItems.Remove(item);
        ItemLookup.Remove(item.Name);
        UnsavedItems.Remove(item);
        ModifiedItems.Remove(item);

        // we currently don't support more than one level of children
        foreach (var childItem in item.Children)
        {
            ItemLookup.Remove(childItem.Name);
            UnsavedItems.Remove(childItem);
            ModifiedItems.Remove(childItem);
        }
    }

    public void CloseAll()
    {
        foreach (var item in RootItems)
        {
            if (item.ObjectType == WorkspaceItemType.ResourceFile && item.Loaded)
            {
                var stream = (Stream)item.Object;
                stream.Close();
            }
        }
        Manager.UnloadAll();
        Manager.UnloadClassDatabase();
        _loadedClassDatabaseVersion = null;
        Manager.MonoTempGenerator = null;
        _setMonoTempGeneratorsYet = false;
        RootItems.Clear();
        ItemLookup.Clear();
        UnsavedItems.Clear();
        ModifiedItems.Clear();
    }

    public void RenameFile(WorkspaceItem wsItem, string newName)
    {
        var oldName = wsItem.Name;
        if (oldName != newName)
        {
            if (wsItem.Object is AssetsFileInstance fileInst)
            {
                fileInst.name = newName;
            }
            else if (wsItem.Object is BundleFileInstance bunInst)
            {
                bunInst.name = newName;
            }

            wsItem.Name = newName;
            wsItem.Update(nameof(wsItem.Name));
            Dirty(wsItem);
            ItemLookup.Remove(oldName);
            ItemLookup[newName] = wsItem;
        }
    }

    public WorkspaceItem? FindWorkspaceItemByInstance(AssetsFileInstance fileInst)
    {
        // todo: keying needs to be a generic method
        var key = fileInst.name;

        if (ItemLookup.TryGetValue(key, out var wsItem))
            return wsItem;

        // no match? try bfs searching starting at the root
        // we pass the null since this is the last resort option
        return FindWorkspaceItemBfs(i =>
            i.Object is AssetsFileInstance thisFileInst && thisFileInst == fileInst
        );
    }

    public WorkspaceItem? FindWorkspaceItemByInstance(BundleFileInstance bunInst)
    {
        // todo: keying needs to be a generic method
        var key = bunInst.name;

        if (ItemLookup.TryGetValue(key, out var wsItem))
            return wsItem;

        return FindWorkspaceItemBfs(i =>
            i.Object is BundleFileInstance thisBunInst && thisBunInst == bunInst
        );
    }

    private WorkspaceItem? FindWorkspaceItemBfs(Func<WorkspaceItem, bool> predicate)
    {
        var searchQueue = new Queue<WorkspaceItem>(RootItems);
        while (searchQueue.Count > 0)
        {
            var current = searchQueue.Dequeue();

            if (predicate(current))
                return current;

            if (current.Children != null)
            {
                foreach (var child in current.Children)
                    searchQueue.Enqueue(child);
            }
        }

        return null;
    }
}
