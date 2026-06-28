using AssetsTools.NET.Extra;

namespace UnityComponentPlugin;

internal static class UnityComponentTypes
{
    public static readonly AssetClassID[] PreviewTypes =
    [
        AssetClassID.GameObject,
        AssetClassID.Transform,
        AssetClassID.MeshFilter,
        AssetClassID.MeshRenderer,
        AssetClassID.SkinnedMeshRenderer,
        AssetClassID.Shader,
        AssetClassID.MeshCollider,
        AssetClassID.BoxCollider,
        AssetClassID.Rigidbody,
        AssetClassID.Camera,
    ];

    public static bool IsPreviewType(AssetClassID type) =>
        PreviewTypes.Contains(type);

    public static bool IsEditableType(AssetClassID type) =>
        PreviewTypes.Contains(type);
}