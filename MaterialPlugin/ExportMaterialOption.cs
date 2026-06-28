using AssetsTools.NET.Extra;
using Avalonia.Platform.Storage;
using System.Text;
using UABEANext4.AssetWorkspace;
using UABEANext4.Logic.Configuration;
using UABEANext4.Plugins;
using UABEANext4.Util;

namespace MaterialPlugin;

public class ExportMaterialOption : IUavPluginOption
{
    public string Name => "Export Material";
    public string Description => "Exports Unity Materials to editable JSON";
    public UavPluginMode Options => UavPluginMode.Export;

    public bool SupportsSelection(Workspace workspace, UavPluginMode mode, IList<AssetInst> selection) =>
        mode == UavPluginMode.Export &&
        selection.Count > 0 &&
        selection.All(MaterialAssetHelper.IsMaterial);

    public async Task<bool> Execute(Workspace workspace, IUavPluginFunctions funcs, UavPluginMode mode, IList<AssetInst> selection)
    {
        if (selection.Count > 1)
            return await BatchExport(workspace, funcs, selection);

        return await SingleExport(workspace, funcs, selection[0]);
    }

    private static async Task<bool> BatchExport(Workspace workspace, IUavPluginFunctions funcs, IList<AssetInst> selection)
    {
        var dir = await funcs.ShowOpenFolderDialog(new FolderPickerOpenOptions()
        {
            Title = "Select material export directory"
        });

        if (dir is null)
            return false;

        var errorBuilder = new StringBuilder();
        var exportJustNames = ConfigurationManager.Settings.ExportImportJustNames;

        foreach (var asset in selection)
        {
            var errorAssetName = $"{Path.GetFileName(asset.FileInstance.path)}/{asset.PathId}";
            try
            {
                var bf = workspace.GetBaseField(asset);
                if (bf is null)
                {
                    errorBuilder.AppendLine($"[{errorAssetName}]: failed to read");
                    continue;
                }

                var assetName = PathUtils.ReplaceInvalidPathChars(asset.AssetName ?? asset.DisplayName);
                var fileName = AssetNamer.GetAssetFileName(asset, assetName, ".mat.json", exportJustNames);
                var json = MaterialAssetHelper.ExportToJson(workspace, asset, bf);
                await File.WriteAllTextAsync(Path.Combine(dir, fileName), json);
            }
            catch (Exception ex)
            {
                errorBuilder.AppendLine($"[{errorAssetName}]: {ex.Message}");
            }
        }

        if (errorBuilder.Length > 0)
        {
            var firstLines = errorBuilder.ToString().Split('\n').Take(20).ToArray();
            await funcs.ShowMessageDialog("Material Export Error", string.Join('\n', firstLines));
        }

        return errorBuilder.Length == 0;
    }

    private static async Task<bool> SingleExport(Workspace workspace, IUavPluginFunctions funcs, AssetInst asset)
    {
        var assetName = PathUtils.ReplaceInvalidPathChars(asset.AssetName ?? asset.DisplayName);
        var exportJustNames = ConfigurationManager.Settings.ExportImportJustNames;
        var filePath = await funcs.ShowSaveFileDialog(new FilePickerSaveOptions()
        {
            Title = "Save material JSON",
            FileTypeChoices =
            [
                new("Material JSON (*.mat.json)") { Patterns = ["*.mat.json"] },
                new("JSON file (*.json)") { Patterns = ["*.json"] },
                new("All types (*.*)") { Patterns = ["*"] }
            ],
            SuggestedFileName = AssetNamer.GetAssetFileName(asset, assetName, string.Empty, exportJustNames),
            DefaultExtension = "mat.json"
        });

        if (filePath is null)
            return false;

        try
        {
            var bf = workspace.GetBaseField(asset);
            if (bf is null)
            {
                await funcs.ShowMessageDialog("Material Export Error", "Failed to read material.");
                return false;
            }

            var json = MaterialAssetHelper.ExportToJson(workspace, asset, bf);
            await File.WriteAllTextAsync(filePath, json);
            return true;
        }
        catch (Exception ex)
        {
            await funcs.ShowMessageDialog("Material Export Error", ex.Message);
            return false;
        }
    }
}