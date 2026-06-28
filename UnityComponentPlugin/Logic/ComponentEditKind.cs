using AssetsTools.NET.Extra;

namespace UnityComponentPlugin.Logic;

public enum ComponentEditKind
{
    GameObject,
    Transform,
    MeshFilter,
    MeshRenderer,
    Shader,
    MeshCollider,
    BoxCollider,
    Rigidbody,
    Camera,
}

internal static class ComponentEditKindExtensions
{
    public static ComponentEditKind? FromAssetType(AssetClassID type) => type switch
    {
        AssetClassID.GameObject => ComponentEditKind.GameObject,
        AssetClassID.Transform => ComponentEditKind.Transform,
        AssetClassID.MeshFilter => ComponentEditKind.MeshFilter,
        AssetClassID.MeshRenderer => ComponentEditKind.MeshRenderer,
        AssetClassID.SkinnedMeshRenderer => ComponentEditKind.MeshRenderer,
        AssetClassID.Shader => ComponentEditKind.Shader,
        AssetClassID.MeshCollider => ComponentEditKind.MeshCollider,
        AssetClassID.BoxCollider => ComponentEditKind.BoxCollider,
        AssetClassID.Rigidbody => ComponentEditKind.Rigidbody,
        AssetClassID.Camera => ComponentEditKind.Camera,
        _ => null
    };
}