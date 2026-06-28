using Avalonia.Media.Imaging;
using UABEANext4.AssetWorkspace;
using UABEANext4.Logic.Mesh;
using UABEANext4.Plugins;

namespace MaterialPlugin;

public class MaterialPreviewer : IUavPluginPreviewer
{
    public string Name => "Preview Material";
    public string Description => "Preview Unity Material shader, keywords, and saved properties";

    public UavPluginPreviewerType SupportsPreview(Workspace workspace, AssetInst selection) =>
        MaterialAssetHelper.IsMaterial(selection)
            ? UavPluginPreviewerType.Text
            : UavPluginPreviewerType.None;

    public string? ExecuteText(Workspace workspace, IUavPluginFunctions funcs, AssetInst selection, out string? error)
    {
        try
        {
            var bf = workspace.GetBaseField(selection);
            if (bf is null)
            {
                error = "Could not load material fields.";
                return null;
            }

            error = null;
            return MaterialAssetHelper.FormatPreview(workspace, selection, bf);
        }
        catch (Exception ex)
        {
            error = $"Material preview failed: {ex.Message}";
            return null;
        }
    }

    public (Bitmap?, int) ExecuteImage(Workspace workspace, IUavPluginFunctions funcs, AssetInst selection, out string? error)
        => throw new InvalidOperationException();

    public MeshObj? ExecuteMesh(Workspace workspace, IUavPluginFunctions funcs, AssetInst selection, out string? error)
        => throw new InvalidOperationException();

    public void Cleanup() { }
}