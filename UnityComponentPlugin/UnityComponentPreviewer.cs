using AssetsTools.NET.Extra;
using Avalonia.Media.Imaging;
using UABEANext4.AssetWorkspace;
using UABEANext4.Logic.Mesh;
using UABEANext4.Plugins;

namespace UnityComponentPlugin;

public class UnityComponentPreviewer : IUavPluginPreviewer
{
    public string Name => "Preview Unity Components";
    public string Description => "Preview GameObject, Transform, colliders, physics, camera, shader, and mesh components";

    public UavPluginPreviewerType SupportsPreview(Workspace workspace, AssetInst selection) =>
        UnityComponentTypes.IsPreviewType(selection.Type)
            ? UavPluginPreviewerType.Text
            : UavPluginPreviewerType.None;

    public string? ExecuteText(Workspace workspace, IUavPluginFunctions funcs, AssetInst selection, out string? error)
    {
        try
        {
            var bf = workspace.GetBaseField(selection);
            if (bf is null)
            {
                error = "Could not load asset fields.";
                return null;
            }

            error = null;
            return ComponentPreviewFormatter.Format(workspace, selection, bf);
        }
        catch (Exception ex)
        {
            error = $"Component preview failed: {ex.Message}";
            return null;
        }
    }

    public (Bitmap?, int) ExecuteImage(Workspace workspace, IUavPluginFunctions funcs, AssetInst selection, out string? error)
        => throw new InvalidOperationException();

    public MeshObj? ExecuteMesh(Workspace workspace, IUavPluginFunctions funcs, AssetInst selection, out string? error)
        => throw new InvalidOperationException();

    public void Cleanup() { }
}