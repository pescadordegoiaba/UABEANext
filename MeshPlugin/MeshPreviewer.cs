using Avalonia.Media.Imaging;
using UABEANext4.AssetWorkspace;
using UABEANext4.Logic.Mesh;
using UABEANext4.Plugins;

namespace MeshPlugin;
public class MeshPreviewer : IUavPluginPreviewer
{
    public string Name => "Preview Mesh";
    public string Description => "Preview Meshes";

    public UavPluginPreviewerType SupportsPreview(Workspace workspace, AssetInst selection)
    {
        var previewType = MeshAssetHelper.SupportsMeshSelection(workspace, selection)
            ? UavPluginPreviewerType.Mesh
            : UavPluginPreviewerType.None;

        return previewType;
    }

    public MeshObj? ExecuteMesh(Workspace workspace, IUavPluginFunctions funcs, AssetInst selection, out string? error)
    {
        try
        {
            return MeshAssetHelper.ReadMesh(workspace, selection, out error);
        }
        catch (Exception ex)
        {
            error = $"Mesh failed to decode due to an error. Exception:\n{ex}";
            return null;
        }
    }

    public (Bitmap?, int) ExecuteImage(Workspace workspace, IUavPluginFunctions funcs, AssetInst selection, out string? error)
        => throw new InvalidOperationException();

    public string? ExecuteText(Workspace workspace, IUavPluginFunctions funcs, AssetInst selection, out string? error)
        => throw new InvalidOperationException();

    public void Cleanup() { }
}
