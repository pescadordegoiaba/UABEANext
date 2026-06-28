using AssetsTools.NET.Extra;
using UABEANext4.AssetWorkspace;
using UABEANext4.Logic.Mesh;

namespace MeshPlugin;

internal static class MeshAssetHelper
{
    public static bool SupportsMeshSelection(Workspace workspace, AssetInst asset)
    {
        return asset.Type == AssetClassID.Mesh
            || TryGetMeshAsset(workspace, asset, out _, out _) ;
    }

    public static bool TryGetMeshAsset(Workspace workspace, AssetInst selection, out AssetInst? meshAsset, out string? error)
    {
        meshAsset = null;
        error = null;

        switch (selection.Type)
        {
            case AssetClassID.Mesh:
                meshAsset = selection;
                return true;

            case AssetClassID.GameObject:
                meshAsset = GetMeshFromGameObject(workspace, selection);
                if (meshAsset is not null)
                    return true;
                error = "No mesh was found on the selected GameObject (MeshFilter / SkinnedMeshRenderer).";
                return false;

            case AssetClassID.MeshFilter:
                return TryGetMeshFromComponent(workspace, selection, "m_Mesh", out meshAsset, out error);

            case AssetClassID.MeshRenderer:
            case AssetClassID.SkinnedMeshRenderer:
                return TryGetMeshFromComponent(workspace, selection, "m_Mesh", out meshAsset, out error)
                    || TryGetMeshFromGameObjectOnComponent(workspace, selection, out meshAsset, out error);

            default:
                error = "Selection is not a mesh or a component that references a mesh.";
                return false;
        }
    }

    public static MeshObj? ReadMesh(Workspace workspace, AssetInst selection, out string? error)
    {
        if (!TryGetMeshAsset(workspace, selection, out var meshAsset, out error) || meshAsset is null)
            return null;

        var meshBf = workspace.GetBaseField(meshAsset);
        if (meshBf is null)
        {
            error = "Mesh base field couldn't be loaded.";
            return null;
        }

        var version = new UnityVersion(meshAsset.FileInstance.file.Metadata.UnityVersion);
        try
        {
            error = null;
            return new MeshObj(meshAsset.FileInstance, meshBf, version);
        }
        catch (Exception ex)
        {
            error = $"Mesh failed to decode: {ex.Message}";
            return null;
        }
    }

    private static bool TryGetMeshFromComponent(
        Workspace workspace,
        AssetInst componentAsset,
        string meshFieldName,
        out AssetInst? meshAsset,
        out string? error)
    {
        meshAsset = null;
        error = null;

        var componentBf = workspace.GetBaseField(componentAsset);
        if (componentBf is null)
        {
            error = "Component fields could not be loaded.";
            return false;
        }

        var meshPtr = componentBf[meshFieldName];
        if (meshPtr.IsDummy)
        {
            error = $"Component has no {meshFieldName} reference.";
            return false;
        }

        meshAsset = workspace.GetAssetInst(componentAsset.FileInstance, meshPtr);
        if (meshAsset is null)
        {
            error = "Mesh reference could not be resolved. Load dependencies if needed.";
            return false;
        }

        return true;
    }

    private static bool TryGetMeshFromGameObjectOnComponent(
        Workspace workspace,
        AssetInst componentAsset,
        out AssetInst? meshAsset,
        out string? error)
    {
        meshAsset = null;
        error = null;

        var componentBf = workspace.GetBaseField(componentAsset);
        if (componentBf is null)
            return false;

        var goPtr = componentBf["m_GameObject"];
        if (goPtr.IsDummy)
            return false;

        var goAsset = workspace.GetAssetInst(componentAsset.FileInstance, goPtr);
        if (goAsset is null)
            return false;

        meshAsset = GetMeshFromGameObject(workspace, goAsset);
        if (meshAsset is null)
        {
            error = "GameObject on this renderer has no MeshFilter/SkinnedMeshRenderer mesh.";
            return false;
        }

        return true;
    }

    private static AssetInst? GetMeshFromGameObject(Workspace workspace, AssetInst goAsset)
    {
        if (goAsset.Type != AssetClassID.GameObject)
            return null;

        var goBase = workspace.GetBaseField(goAsset);
        if (goBase is null)
            return null;

        AssetInst? skinnedMesh = null;
        var goComponents = goBase["m_Component.Array"];
        foreach (var componentPair in goComponents)
        {
            var component = componentPair[componentPair.Children.Count - 1];
            var componentInf = workspace.GetAssetFileInfo(goAsset.FileInstance, component);
            if (componentInf is null)
                continue;

            if (componentInf.TypeId == (int)AssetClassID.MeshFilter)
            {
                var mfiltAsset = new AssetInst(goAsset.FileInstance, componentInf);
                var mfiltBase = workspace.GetBaseField(mfiltAsset);
                if (mfiltBase is null)
                    return null;

                return workspace.GetAssetInst(mfiltAsset.FileInstance, mfiltBase["m_Mesh"]);
            }

            if (componentInf.TypeId == (int)AssetClassID.SkinnedMeshRenderer)
            {
                var smrAsset = new AssetInst(goAsset.FileInstance, componentInf);
                var smrBase = workspace.GetBaseField(smrAsset);
                if (smrBase is null)
                    continue;

                skinnedMesh = workspace.GetAssetInst(smrAsset.FileInstance, smrBase["m_Mesh"]);
            }
        }

        return skinnedMesh;
    }
}