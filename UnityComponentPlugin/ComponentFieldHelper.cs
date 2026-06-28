using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System.Globalization;
using System.Numerics;
using System.Text;
using UABEANext4.AssetWorkspace;

namespace UnityComponentPlugin;

internal static class ComponentFieldHelper
{
    public static bool TryGetString(AssetTypeValueField baseField, string fieldName, out string value)
    {
        var f = baseField[fieldName];
        if (f.IsDummy)
        {
            value = string.Empty;
            return false;
        }

        value = f.AsString;
        return true;
    }

    public static bool TryGetBool(AssetTypeValueField baseField, string fieldName, out bool value)
    {
        var f = baseField[fieldName];
        if (f.IsDummy)
        {
            value = false;
            return false;
        }

        value = f.AsBool;
        return true;
    }

    public static bool TryGetInt(AssetTypeValueField baseField, string fieldName, out int value)
    {
        var f = baseField[fieldName];
        if (f.IsDummy)
        {
            value = 0;
            return false;
        }

        value = f.AsInt;
        return true;
    }

    public static bool TryGetFloat(AssetTypeValueField baseField, string fieldName, out float value)
    {
        var f = baseField[fieldName];
        if (f.IsDummy)
        {
            value = 0;
            return false;
        }

        value = f.AsFloat;
        return true;
    }

    public static bool TryGetVector3(AssetTypeValueField baseField, string fieldName, out Vector3 value)
    {
        var f = baseField[fieldName];
        if (f.IsDummy || f.Children.Count < 3)
        {
            value = Vector3.Zero;
            return false;
        }

        value = new Vector3(
            f["x"].AsFloat,
            f["y"].AsFloat,
            f["z"].AsFloat);
        return true;
    }

    public static bool TryGetQuaternion(AssetTypeValueField baseField, string fieldName, out Quaternion value)
    {
        var f = baseField[fieldName];
        if (f.IsDummy || f.Children.Count < 4)
        {
            value = Quaternion.Identity;
            return false;
        }

        value = new Quaternion(
            f["x"].AsFloat,
            f["y"].AsFloat,
            f["z"].AsFloat,
            f["w"].AsFloat);
        return true;
    }

    public static void SetString(AssetTypeValueField baseField, string fieldName, string value)
    {
        var f = baseField[fieldName];
        if (!f.IsDummy)
            f.AsString = value;
    }

    public static void SetBool(AssetTypeValueField baseField, string fieldName, bool value)
    {
        var f = baseField[fieldName];
        if (!f.IsDummy)
            f.AsBool = value;
    }

    public static void SetInt(AssetTypeValueField baseField, string fieldName, int value)
    {
        var f = baseField[fieldName];
        if (!f.IsDummy)
            f.AsInt = value;
    }

    public static void SetFloat(AssetTypeValueField baseField, string fieldName, float value)
    {
        var f = baseField[fieldName];
        if (!f.IsDummy)
            f.AsFloat = value;
    }

    public static void SetVector3(AssetTypeValueField baseField, string fieldName, Vector3 value)
    {
        var f = baseField[fieldName];
        if (f.IsDummy)
            return;

        if (!f["x"].IsDummy) f["x"].AsFloat = value.X;
        if (!f["y"].IsDummy) f["y"].AsFloat = value.Y;
        if (!f["z"].IsDummy) f["z"].AsFloat = value.Z;
    }

    public static void SetQuaternion(AssetTypeValueField baseField, string fieldName, Quaternion value)
    {
        var f = baseField[fieldName];
        if (f.IsDummy)
            return;

        if (!f["x"].IsDummy) f["x"].AsFloat = value.X;
        if (!f["y"].IsDummy) f["y"].AsFloat = value.Y;
        if (!f["z"].IsDummy) f["z"].AsFloat = value.Z;
        if (!f["w"].IsDummy) f["w"].AsFloat = value.W;
    }

    public static void AppendPPtr(StringBuilder sb, Workspace workspace, AssetsFileInstance fileInst, AssetTypeValueField pptrField, string label)
    {
        if (pptrField.IsDummy)
            return;

        var pathId = pptrField["m_PathID"].IsDummy ? 0L : pptrField["m_PathID"].AsLong;
        var fileId = pptrField["m_FileID"].IsDummy ? 0 : pptrField["m_FileID"].AsInt;
        sb.AppendLine($"{label}: fileId={fileId} pathId={pathId}");

        var target = workspace.GetAssetInst(fileInst, pptrField);
        if (target is not null)
            sb.AppendLine($"  -> {target.Type} \"{target.DisplayName}\"");
        else if (pathId != 0)
            sb.AppendLine("  -> (unresolved — load dependencies?)");
    }

    public static bool TrySetPPtrPathId(AssetTypeValueField pptrField, long pathId)
    {
        if (pptrField.IsDummy)
            return false;

        if (!pptrField["m_PathID"].IsDummy)
            pptrField["m_PathID"].AsLong = pathId;
        return true;
    }

    public static string FormatVector3(Vector3 v) =>
        $"{v.X.ToString(CultureInfo.InvariantCulture)}, {v.Y.ToString(CultureInfo.InvariantCulture)}, {v.Z.ToString(CultureInfo.InvariantCulture)}";

    public static string FormatQuaternion(Quaternion q) =>
        $"({q.X:F4}, {q.Y:F4}, {q.Z:F4}, {q.W:F4})";

    public static bool TryParseVector3(string text, out Vector3 value)
    {
        value = Vector3.Zero;
        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
            return false;

        if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
            return false;
        if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            return false;
        if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
            return false;

        value = new Vector3(x, y, z);
        return true;
    }
}