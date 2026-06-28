using AssetsTools.NET.Extra;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Messaging;
using UABEANext4.AssetWorkspace;
using UABEANext4.Logic;
using UABEANext4.Logic.Mesh;
using UABEANext4.Plugins;

namespace MeshPlugin;

public class ImportMeshObjOption : IUavPluginOption
{
    public string Name => "Import Mesh OBJ";
    public string Description => "Replaces a Unity Mesh with geometry from a Wavefront OBJ (any vertex/triangle count)";
    public UavPluginMode Options => UavPluginMode.Import;

    public bool SupportsSelection(Workspace workspace, UavPluginMode mode, IList<AssetInst> selection)
    {
        return mode == UavPluginMode.Import &&
            selection.Count == 1 &&
            MeshAssetHelper.SupportsMeshSelection(workspace, selection[0]);
    }

    public async Task<bool> Execute(Workspace workspace, IUavPluginFunctions funcs, UavPluginMode mode, IList<AssetInst> selection)
    {
        var filePaths = await funcs.ShowOpenFileDialog(new FilePickerOpenOptions()
        {
            Title = "Load mesh OBJ",
            FileTypeFilter = new List<FilePickerFileType>()
            {
                new("Wavefront OBJ (*.obj)") { Patterns = ["*.obj"] },
                new("All types (*.*)") { Patterns = ["*"] }
            },
            AllowMultiple = false
        });

        if (filePaths.Length == 0)
        {
            return false;
        }

        var filePath = filePaths[0];
        if (!File.Exists(filePath))
        {
            await funcs.ShowMessageDialog("Mesh Import Error", $"Failed to import because {filePath} does not exist.");
            return false;
        }

        try
        {
            var selectedAsset = selection[0];
            if (!MeshAssetHelper.TryGetMeshAsset(workspace, selectedAsset, out var meshAsset, out var error) || meshAsset is null)
            {
                await funcs.ShowMessageDialog("Mesh Import Error", error ?? "Selected asset has no mesh.");
                return false;
            }

            var baseField = workspace.GetBaseField(meshAsset);
            if (baseField is null)
            {
                await funcs.ShowMessageDialog("Mesh Import Error", "Mesh base field couldn't be loaded.");
                return false;
            }

            var version = new UnityVersion(meshAsset.FileInstance.file.Metadata.UnityVersion);
            var mesh = new MeshObj(meshAsset.FileInstance, baseField, version);
            var imported = MeshObjImporter.ReadMesh(filePath);
            mesh.WriteImportedMesh(meshAsset.FileInstance, baseField, version, imported);
            meshAsset.UpdateAssetDataAndRow(workspace, baseField);

            if (HasSkinningChannels(mesh))
            {
                await funcs.ShowMessageDialog("Mesh Import Warning",
                    "This mesh uses skinning channels (bone weights/indices). " +
                    "Geometry was replaced, but bone weights were reset to defaults. " +
                    "Skinned characters may need manual fixes in Unity.");
            }

            WeakReferenceMessenger.Default.Send(new AssetsUpdatedMessage(meshAsset));
            if (!ReferenceEquals(meshAsset, selectedAsset))
            {
                WeakReferenceMessenger.Default.Send(new AssetsUpdatedMessage(selectedAsset));
            }

            return true;
        }
        catch (Exception ex)
        {
            await funcs.ShowMessageDialog("Mesh Import Error", ex.Message);
            return false;
        }
    }

    private static bool HasSkinningChannels(MeshObj mesh)
    {
        if (mesh.Channels.Count <= (int)ChannelTypeV3.BlendWeight)
        {
            return false;
        }

        var weights = mesh.Channels[(int)ChannelTypeV3.BlendWeight];
        var indices = mesh.Channels[(int)ChannelTypeV3.BlendIndices];
        return (weights.dimension & 0xf) > 0 || (indices.dimension & 0xf) > 0;
    }
}
