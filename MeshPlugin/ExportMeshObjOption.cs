using Avalonia.Platform.Storage;
using System.Text;
using UABEANext4.AssetWorkspace;
using UABEANext4.Logic.Configuration;
using UABEANext4.Plugins;
using UABEANext4.Util;

namespace MeshPlugin;

public class ExportMeshObjOption : IUavPluginOption
{
    public string Name => "Export Mesh OBJ";
    public string Description => "Exports Mesh/GameObject meshes to Wavefront OBJ for Blender";
    public UavPluginMode Options => UavPluginMode.Export;

    public bool SupportsSelection(Workspace workspace, UavPluginMode mode, IList<AssetInst> selection)
    {
        if (mode != UavPluginMode.Export || selection.Count == 0)
        {
            return false;
        }

        return selection.All(asset => MeshAssetHelper.SupportsMeshSelection(workspace, asset));
    }

    public async Task<bool> Execute(Workspace workspace, IUavPluginFunctions funcs, UavPluginMode mode, IList<AssetInst> selection)
    {
        if (selection.Count > 1)
        {
            return await BatchExport(workspace, funcs, selection);
        }

        return await SingleExport(workspace, funcs, selection[0]);
    }

    private static async Task<bool> BatchExport(Workspace workspace, IUavPluginFunctions funcs, IList<AssetInst> selection)
    {
        var dir = await funcs.ShowOpenFolderDialog(new FolderPickerOpenOptions()
        {
            Title = "Select mesh export directory"
        });

        if (dir is null)
        {
            return false;
        }

        var errorBuilder = new StringBuilder();
        var exportJustNames = ConfigurationManager.Settings.ExportImportJustNames;
        foreach (var asset in selection)
        {
            var errorAssetName = $"{Path.GetFileName(asset.FileInstance.path)}/{asset.PathId}";
            try
            {
                var mesh = MeshAssetHelper.ReadMesh(workspace, asset, out var error);
                if (mesh is null)
                {
                    errorBuilder.AppendLine($"[{errorAssetName}]: {error ?? "failed to read mesh"}");
                    continue;
                }

                var assetName = PathUtils.ReplaceInvalidPathChars(asset.AssetName ?? asset.DisplayName);
                var fileName = AssetNamer.GetAssetFileName(asset, assetName, ".obj", exportJustNames);
                MeshObjExporter.Export(Path.Combine(dir, fileName), mesh, assetName);
            }
            catch (Exception ex)
            {
                errorBuilder.AppendLine($"[{errorAssetName}]: {ex.Message}");
            }
        }

        if (errorBuilder.Length > 0)
        {
            var firstLines = errorBuilder.ToString().Split('\n').Take(20).ToArray();
            await funcs.ShowMessageDialog("Mesh Export Error", string.Join('\n', firstLines));
        }

        return errorBuilder.Length == 0;
    }

    private static async Task<bool> SingleExport(Workspace workspace, IUavPluginFunctions funcs, AssetInst asset)
    {
        var assetName = PathUtils.ReplaceInvalidPathChars(asset.AssetName ?? asset.DisplayName);
        var exportJustNames = ConfigurationManager.Settings.ExportImportJustNames;
        var filePath = await funcs.ShowSaveFileDialog(new FilePickerSaveOptions()
        {
            Title = "Save mesh OBJ",
            FileTypeChoices = new List<FilePickerFileType>()
            {
                new("Wavefront OBJ (*.obj)") { Patterns = ["*.obj"] },
                new("All types (*.*)") { Patterns = ["*"] }
            },
            SuggestedFileName = AssetNamer.GetAssetFileName(asset, assetName, string.Empty, exportJustNames),
            DefaultExtension = "obj"
        });

        if (filePath is null)
        {
            return false;
        }

        try
        {
            var mesh = MeshAssetHelper.ReadMesh(workspace, asset, out var error);
            if (mesh is null)
            {
                await funcs.ShowMessageDialog("Mesh Export Error", error ?? "Failed to read mesh.");
                return false;
            }

            MeshObjExporter.Export(filePath, mesh, assetName);
            return true;
        }
        catch (Exception ex)
        {
            await funcs.ShowMessageDialog("Mesh Export Error", ex.Message);
            return false;
        }
    }
}
