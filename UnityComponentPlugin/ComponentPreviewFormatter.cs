using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System.Text;
using UABEANext4.AssetWorkspace;

namespace UnityComponentPlugin;

internal static class ComponentPreviewFormatter
{
    public static string? Format(Workspace workspace, AssetInst asset, AssetTypeValueField bf)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Type: {asset.Type}");
        sb.AppendLine($"File: {asset.FileName}");
        sb.AppendLine($"Path ID: {asset.PathId}");
        sb.AppendLine();

        switch (asset.Type)
        {
            case AssetClassID.GameObject:
                FormatGameObject(workspace, asset, bf, sb);
                break;
            case AssetClassID.Transform:
                FormatTransform(workspace, asset, bf, sb);
                break;
            case AssetClassID.MeshFilter:
                FormatMeshFilter(workspace, asset, bf, sb);
                break;
            case AssetClassID.MeshRenderer:
            case AssetClassID.SkinnedMeshRenderer:
                FormatMeshRenderer(workspace, asset, bf, sb);
                break;
            case AssetClassID.Shader:
                FormatShader(bf, sb);
                break;
            case AssetClassID.MeshCollider:
                FormatMeshCollider(workspace, asset, bf, sb);
                break;
            case AssetClassID.BoxCollider:
                FormatBoxCollider(bf, sb);
                break;
            case AssetClassID.Rigidbody:
                FormatRigidbody(bf, sb);
                break;
            case AssetClassID.Camera:
                FormatCamera(bf, sb);
                break;
            default:
                sb.AppendLine("(No dedicated formatter for this type.)");
                break;
        }

        sb.AppendLine();
        sb.AppendLine("Use Plugins → Edit Unity Component to modify supported fields.");
        return sb.ToString().TrimEnd();
    }

    private static void FormatGameObject(Workspace workspace, AssetInst asset, AssetTypeValueField bf, StringBuilder sb)
    {
        if (ComponentFieldHelper.TryGetString(bf, "m_Name", out var name))
            sb.AppendLine($"Name: {name}");
        if (ComponentFieldHelper.TryGetBool(bf, "m_IsActive", out var active))
            sb.AppendLine($"Active: {active}");
        if (ComponentFieldHelper.TryGetInt(bf, "m_Layer", out var layer))
            sb.AppendLine($"Layer: {layer}");
        if (ComponentFieldHelper.TryGetString(bf, "m_TagString", out var tag))
            sb.AppendLine($"Tag: {tag}");
        else if (ComponentFieldHelper.TryGetInt(bf, "m_Tag", out var tagId))
            sb.AppendLine($"Tag ID: {tagId}");

        var components = bf["m_Component.Array"];
        if (!components.IsDummy)
        {
            sb.AppendLine($"Components ({components.Children.Count}):");
            foreach (var pair in components)
            {
                var comp = pair[pair.Children.Count - 1];
                var compInf = workspace.GetAssetFileInfo(asset.FileInstance, comp);
                if (compInf is null)
                    continue;
                sb.AppendLine($"  - {(AssetClassID)compInf.TypeId} pathId={compInf.PathId}");
            }
        }

        var transformPtr = workspace.GetAssetInst(asset.FileInstance, bf["m_Transform"]);
        if (transformPtr is not null && ComponentFieldHelper.TryGetVector3(
                workspace.GetBaseField(transformPtr)!, "m_LocalPosition", out var pos))
        {
            sb.AppendLine($"Transform position: {ComponentFieldHelper.FormatVector3(pos)}");
        }
    }

    private static void FormatTransform(Workspace workspace, AssetInst asset, AssetTypeValueField bf, StringBuilder sb)
    {
        if (ComponentFieldHelper.TryGetVector3(bf, "m_LocalPosition", out var pos))
            sb.AppendLine($"Local Position: {ComponentFieldHelper.FormatVector3(pos)}");
        if (ComponentFieldHelper.TryGetQuaternion(bf, "m_LocalRotation", out var rot))
            sb.AppendLine($"Local Rotation: {ComponentFieldHelper.FormatQuaternion(rot)}");
        if (ComponentFieldHelper.TryGetVector3(bf, "m_LocalScale", out var scale))
            sb.AppendLine($"Local Scale: {ComponentFieldHelper.FormatVector3(scale)}");

        ComponentFieldHelper.AppendPPtr(sb, workspace, asset.FileInstance, bf["m_Father"], "Father");
        ComponentFieldHelper.AppendPPtr(sb, workspace, asset.FileInstance, bf["m_GameObject"], "GameObject");

        var children = bf["m_Children.Array"];
        if (!children.IsDummy && children.Children.Count > 0)
        {
            sb.AppendLine($"Children ({children.Children.Count}):");
            foreach (var child in children)
            {
                var childInst = workspace.GetAssetInst(asset.FileInstance, child);
                if (childInst is not null)
                    sb.AppendLine($"  - Transform pathId={childInst.PathId} ({childInst.DisplayName})");
            }
        }
    }

    private static void FormatMeshFilter(Workspace workspace, AssetInst asset, AssetTypeValueField bf, StringBuilder sb)
    {
        ComponentFieldHelper.AppendPPtr(sb, workspace, asset.FileInstance, bf["m_Mesh"], "Mesh");
        ComponentFieldHelper.AppendPPtr(sb, workspace, asset.FileInstance, bf["m_GameObject"], "GameObject");
        sb.AppendLine();
        sb.AppendLine("3D preview: select this asset when the mesh reference resolves (Previewer panel).");
    }

    private static void FormatMeshRenderer(Workspace workspace, AssetInst asset, AssetTypeValueField bf, StringBuilder sb)
    {
        if (ComponentFieldHelper.TryGetBool(bf, "m_Enabled", out var enabled))
            sb.AppendLine($"Enabled: {enabled}");
        if (ComponentFieldHelper.TryGetBool(bf, "m_CastShadows", out var castShadows))
            sb.AppendLine($"Cast Shadows: {castShadows}");
        if (ComponentFieldHelper.TryGetBool(bf, "m_ReceiveShadows", out var recvShadows))
            sb.AppendLine($"Receive Shadows: {recvShadows}");

        ComponentFieldHelper.AppendPPtr(sb, workspace, asset.FileInstance, bf["m_GameObject"], "GameObject");

        var materials = bf["m_Materials.Array"];
        if (!materials.IsDummy)
        {
            sb.AppendLine($"Materials ({materials.Children.Count}):");
            var i = 0;
            foreach (var mat in materials)
            {
                ComponentFieldHelper.AppendPPtr(sb, workspace, asset.FileInstance, mat, $"  [{i}]");
                i++;
            }
        }

        if (asset.Type == AssetClassID.SkinnedMeshRenderer)
        {
            ComponentFieldHelper.AppendPPtr(sb, workspace, asset.FileInstance, bf["m_Mesh"], "Skinned Mesh");
            sb.AppendLine("Type: SkinnedMeshRenderer");
        }
        else
        {
            sb.AppendLine("Type: MeshRenderer");
        }

        sb.AppendLine();
        sb.AppendLine("3D preview: opens linked Mesh in the Previewer when available.");
    }

    private static void FormatShader(AssetTypeValueField bf, StringBuilder sb)
    {
        if (ComponentFieldHelper.TryGetString(bf, "m_Name", out var name))
            sb.AppendLine($"Name: {name}");

        var parsed = bf["m_ParsedForm"];
        if (!parsed.IsDummy)
        {
            if (ComponentFieldHelper.TryGetString(parsed, "m_Name", out var parsedName))
                sb.AppendLine($"Parsed Name: {parsedName}");

            var subShaders = parsed["m_SubShaders.Array"];
            if (!subShaders.IsDummy)
                sb.AppendLine($"SubShaders: {subShaders.Children.Count}");
        }

        var script = bf["m_Script"];
        if (!script.IsDummy && script.AsByteArray.Length > 0)
            sb.AppendLine($"Shader bytecode size: {script.AsByteArray.Length} bytes");

        sb.AppendLine();
        sb.AppendLine("Edit: shader display name (m_Name) can be changed via Edit Unity Component.");
    }

    private static void FormatMeshCollider(Workspace workspace, AssetInst asset, AssetTypeValueField bf, StringBuilder sb)
    {
        if (ComponentFieldHelper.TryGetBool(bf, "m_IsTrigger", out var trigger))
            sb.AppendLine($"Is Trigger: {trigger}");
        if (ComponentFieldHelper.TryGetBool(bf, "m_Convex", out var convex))
            sb.AppendLine($"Convex: {convex}");
        if (ComponentFieldHelper.TryGetBool(bf, "m_CookingOptions", out _))
        { }
        if (ComponentFieldHelper.TryGetBool(bf, "m_Enabled", out var enabled))
            sb.AppendLine($"Enabled: {enabled}");

        ComponentFieldHelper.AppendPPtr(sb, workspace, asset.FileInstance, bf["m_Mesh"], "Collider Mesh");
        ComponentFieldHelper.AppendPPtr(sb, workspace, asset.FileInstance, bf["m_GameObject"], "GameObject");
    }

    private static void FormatBoxCollider(AssetTypeValueField bf, StringBuilder sb)
    {
        if (ComponentFieldHelper.TryGetBool(bf, "m_IsTrigger", out var trigger))
            sb.AppendLine($"Is Trigger: {trigger}");
        if (ComponentFieldHelper.TryGetBool(bf, "m_Enabled", out var enabled))
            sb.AppendLine($"Enabled: {enabled}");
        if (ComponentFieldHelper.TryGetVector3(bf, "m_Size", out var size))
            sb.AppendLine($"Size: {ComponentFieldHelper.FormatVector3(size)}");
        if (ComponentFieldHelper.TryGetVector3(bf, "m_Center", out var center))
            sb.AppendLine($"Center: {ComponentFieldHelper.FormatVector3(center)}");
    }

    private static void FormatRigidbody(AssetTypeValueField bf, StringBuilder sb)
    {
        if (ComponentFieldHelper.TryGetFloat(bf, "m_Mass", out var mass))
            sb.AppendLine($"Mass: {mass}");
        if (ComponentFieldHelper.TryGetFloat(bf, "m_Drag", out var drag))
            sb.AppendLine($"Drag: {drag}");
        if (ComponentFieldHelper.TryGetFloat(bf, "m_AngularDrag", out var angDrag))
            sb.AppendLine($"Angular Drag: {angDrag}");
        if (ComponentFieldHelper.TryGetBool(bf, "m_UseGravity", out var gravity))
            sb.AppendLine($"Use Gravity: {gravity}");
        if (ComponentFieldHelper.TryGetBool(bf, "m_IsKinematic", out var kinematic))
            sb.AppendLine($"Is Kinematic: {kinematic}");
        if (ComponentFieldHelper.TryGetBool(bf, "m_IsTrigger", out _))
        { }
    }

    private static void FormatCamera(AssetTypeValueField bf, StringBuilder sb)
    {
        if (ComponentFieldHelper.TryGetBool(bf, "m_Enabled", out var enabled))
            sb.AppendLine($"Enabled: {enabled}");
        if (ComponentFieldHelper.TryGetFloat(bf, "m_FieldOfView", out var fov))
            sb.AppendLine($"Field of View: {fov}");
        if (ComponentFieldHelper.TryGetFloat(bf, "m_NearClipPlane", out var near))
            sb.AppendLine($"Near Clip: {near}");
        if (ComponentFieldHelper.TryGetFloat(bf, "m_FarClipPlane", out var far))
            sb.AppendLine($"Far Clip: {far}");
        if (ComponentFieldHelper.TryGetBool(bf, "m_Orthographic", out var ortho))
            sb.AppendLine($"Orthographic: {ortho}");
        if (ComponentFieldHelper.TryGetFloat(bf, "m_OrthographicSize", out var orthoSize))
            sb.AppendLine($"Orthographic Size: {orthoSize}");
        if (ComponentFieldHelper.TryGetInt(bf, "m_ClearFlags", out var clearFlags))
            sb.AppendLine($"Clear Flags: {clearFlags}");
        if (ComponentFieldHelper.TryGetFloat(bf, "m_Depth", out var depth))
            sb.AppendLine($"Depth: {depth}");
    }
}