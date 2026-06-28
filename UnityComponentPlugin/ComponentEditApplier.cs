using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System.Globalization;
using System.Numerics;
using UABEANext4.AssetWorkspace;
using UnityComponentPlugin.Logic;

namespace UnityComponentPlugin;

internal static class ComponentEditApplier
{
    public static bool Apply(Workspace workspace, AssetInst asset, AssetTypeValueField bf, EditComponentResult edit)
    {
        switch (asset.Type)
        {
            case AssetClassID.GameObject:
                if (edit.Name is not null)
                    ComponentFieldHelper.SetString(bf, "m_Name", edit.Name);
                if (edit.IsActive is not null)
                    ComponentFieldHelper.SetBool(bf, "m_IsActive", edit.IsActive.Value);
                if (edit.Layer is not null)
                    ComponentFieldHelper.SetInt(bf, "m_Layer", edit.Layer.Value);
                if (edit.TagString is not null)
                {
                    if (!bf["m_TagString"].IsDummy)
                        ComponentFieldHelper.SetString(bf, "m_TagString", edit.TagString);
                }
                return true;

            case AssetClassID.Transform:
                if (edit.LocalPosition is not null && ComponentFieldHelper.TryParseVector3(edit.LocalPosition, out var pos))
                    ComponentFieldHelper.SetVector3(bf, "m_LocalPosition", pos);
                if (edit.LocalScale is not null && ComponentFieldHelper.TryParseVector3(edit.LocalScale, out var scale))
                    ComponentFieldHelper.SetVector3(bf, "m_LocalScale", scale);
                if (edit.LocalRotation is not null)
                    TryApplyEulerRotation(bf, edit.LocalRotation);
                return true;

            case AssetClassID.MeshFilter:
            case AssetClassID.MeshCollider:
                if (edit.MeshPathId is not null && long.TryParse(edit.MeshPathId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var meshPathId))
                    ComponentFieldHelper.TrySetPPtrPathId(bf["m_Mesh"], meshPathId);
                return true;

            case AssetClassID.MeshRenderer:
            case AssetClassID.SkinnedMeshRenderer:
                if (edit.Enabled is not null)
                    ComponentFieldHelper.SetBool(bf, "m_Enabled", edit.Enabled.Value);
                if (edit.CastShadows is not null)
                    ComponentFieldHelper.SetBool(bf, "m_CastShadows", edit.CastShadows.Value);
                if (edit.ReceiveShadows is not null)
                    ComponentFieldHelper.SetBool(bf, "m_ReceiveShadows", edit.ReceiveShadows.Value);
                return true;

            case AssetClassID.Shader:
                if (edit.Name is not null)
                    ComponentFieldHelper.SetString(bf, "m_Name", edit.Name);
                return true;

            case AssetClassID.BoxCollider:
                if (edit.IsTrigger is not null)
                    ComponentFieldHelper.SetBool(bf, "m_IsTrigger", edit.IsTrigger.Value);
                if (edit.Enabled is not null)
                    ComponentFieldHelper.SetBool(bf, "m_Enabled", edit.Enabled.Value);
                if (edit.ColliderSize is not null && ComponentFieldHelper.TryParseVector3(edit.ColliderSize, out var size))
                    ComponentFieldHelper.SetVector3(bf, "m_Size", size);
                if (edit.ColliderCenter is not null && ComponentFieldHelper.TryParseVector3(edit.ColliderCenter, out var center))
                    ComponentFieldHelper.SetVector3(bf, "m_Center", center);
                return true;

            case AssetClassID.Rigidbody:
                if (edit.Mass is not null)
                    ComponentFieldHelper.SetFloat(bf, "m_Mass", edit.Mass.Value);
                if (edit.Drag is not null)
                    ComponentFieldHelper.SetFloat(bf, "m_Drag", edit.Drag.Value);
                if (edit.AngularDrag is not null)
                    ComponentFieldHelper.SetFloat(bf, "m_AngularDrag", edit.AngularDrag.Value);
                if (edit.UseGravity is not null)
                    ComponentFieldHelper.SetBool(bf, "m_UseGravity", edit.UseGravity.Value);
                if (edit.IsKinematic is not null)
                    ComponentFieldHelper.SetBool(bf, "m_IsKinematic", edit.IsKinematic.Value);
                return true;

            case AssetClassID.Camera:
                if (edit.Enabled is not null)
                    ComponentFieldHelper.SetBool(bf, "m_Enabled", edit.Enabled.Value);
                if (edit.FieldOfView is not null)
                    ComponentFieldHelper.SetFloat(bf, "m_FieldOfView", edit.FieldOfView.Value);
                if (edit.NearClip is not null)
                    ComponentFieldHelper.SetFloat(bf, "m_NearClipPlane", edit.NearClip.Value);
                if (edit.FarClip is not null)
                    ComponentFieldHelper.SetFloat(bf, "m_FarClipPlane", edit.FarClip.Value);
                if (edit.Orthographic is not null)
                    ComponentFieldHelper.SetBool(bf, "m_Orthographic", edit.Orthographic.Value);
                if (edit.OrthographicSize is not null)
                    ComponentFieldHelper.SetFloat(bf, "m_OrthographicSize", edit.OrthographicSize.Value);
                return true;

            default:
                return false;
        }
    }

    private static void TryApplyEulerRotation(AssetTypeValueField bf, string eulerDegreesText)
    {
        var parts = eulerDegreesText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
            return;

        if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
            return;
        if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            return;
        if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
            return;

        var radX = x * MathF.PI / 180f;
        var radY = y * MathF.PI / 180f;
        var radZ = z * MathF.PI / 180f;

        var q = Quaternion.CreateFromYawPitchRoll(radY, radX, radZ);
        ComponentFieldHelper.SetQuaternion(bf, "m_LocalRotation", q);
    }
}