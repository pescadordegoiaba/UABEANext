using Avalonia.Platform.Storage;
using System.Text;
using UABEANext4.AssetWorkspace;
using UABEANext4.Plugins;
using UABEANext4.ViewModels.Dialogs;

namespace MaterialPlugin;

public class ImportMaterialOption : IUavPluginOption
{
    public string Name => "Import Material";
    public string Description => "Imports editable JSON into Unity Materials";
    public UavPluginMode Options => UavPluginMode.Import;

    public bool SupportsSelection(Workspace workspace, UavPluginMode mode, IList<AssetInst> selection) =>
        mode == UavPluginMode.Import &&
        selection.Count > 0 &&
        selection.All(MaterialAssetHelper.IsMaterial);

    public async Task<bool> Execute(Workspace workspace, IUavPluginFunctions funcs, UavPluginMode mode, IList<AssetInst> selection)
    {
        if (selection.Count > 1)
            return await BatchImport(workspace, funcs, selection);

        return await SingleImport(workspace, funcs, selection[0]);
    }

    private static async Task<bool> BatchImport(Workspace workspace, IUavPluginFunctions funcs, IList<AssetInst> selection)
    {
        var dir = await funcs.ShowOpenFolderDialog(new FolderPickerOpenOptions()
        {
            Title = "Select material import directory"
        });

        if (dir is null)
            return false;

        var batchInfosViewModel = new BatchImportViewModel(workspace, selection.ToList(), dir, ["mat.json", "json"]);
        if (batchInfosViewModel.DataGridItems.Count == 0)
        {
            await funcs.ShowMessageDialog("Material Import Error",
                "No matching files found in the directory. Make sure the file names are in UABEA's format.");
            return false;
        }

        var batchInfosResult = await funcs.ShowDialog(batchInfosViewModel);
        if (batchInfosResult is null)
            return false;

        var errorBuilder = new StringBuilder();
        foreach (var info in batchInfosResult)
        {
            var asset = info.Asset;
            var errorAssetName = $"{Path.GetFileName(asset.FileInstance.path)}/{asset.PathId}";

            try
            {
                if (!TryImportFile(workspace, asset, info.ImportFile, out var error))
                    errorBuilder.AppendLine($"[{errorAssetName}]: {error}");
            }
            catch (Exception ex)
            {
                errorBuilder.AppendLine($"[{errorAssetName}]: {ex.Message}");
            }
        }

        if (errorBuilder.Length > 0)
        {
            var firstLines = errorBuilder.ToString().Split('\n').Take(20).ToArray();
            await funcs.ShowMessageDialog("Material Import Error", string.Join('\n', firstLines));
        }

        return errorBuilder.Length == 0;
    }

    private static async Task<bool> SingleImport(Workspace workspace, IUavPluginFunctions funcs, AssetInst asset)
    {
        var filePaths = await funcs.ShowOpenFileDialog(new FilePickerOpenOptions()
        {
            Title = "Load material JSON",
            FileTypeFilter =
            [
                new("Material JSON (*.mat.json)") { Patterns = ["*.mat.json"] },
                new("JSON file (*.json)") { Patterns = ["*.json"] },
                new("All types (*.*)") { Patterns = ["*"] }
            ],
            AllowMultiple = false
        });

        if (filePaths.Length == 0)
            return false;

        var filePath = filePaths[0];
        if (!File.Exists(filePath))
        {
            await funcs.ShowMessageDialog("Material Import Error", $"Failed to import because {filePath} does not exist.");
            return false;
        }

        try
        {
            if (!TryImportFile(workspace, asset, filePath, out var error))
            {
                await funcs.ShowMessageDialog("Material Import Error", error ?? "Import failed.");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            await funcs.ShowMessageDialog("Material Import Error", ex.Message);
            return false;
        }
    }

    private static bool TryImportFile(Workspace workspace, AssetInst asset, string? filePath, out string? error)
    {
        if (filePath is null || !File.Exists(filePath))
        {
            error = $"Failed to import because {filePath ?? "[null]"} does not exist.";
            return false;
        }

        var bf = workspace.GetBaseField(asset);
        if (bf is null)
        {
            error = "Failed to read material.";
            return false;
        }

        var json = File.ReadAllText(filePath);
        MaterialAssetHelper.ImportFromJson(workspace, asset, bf, json);
        asset.UpdateAssetDataAndRow(workspace, bf);
        error = null;
        return true;
    }
}