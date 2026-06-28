using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UABEANext4.AssetWorkspace;

namespace MaterialPlugin;

public static class MaterialAssetHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static bool IsMaterial(AssetInst asset) => asset.Type == AssetClassID.Material;

    public static MaterialExportData ReadMaterial(Workspace workspace, AssetInst asset, AssetTypeValueField bf)
    {
        var data = new MaterialExportData
        {
            Name = TryGetString(bf, "m_Name") ?? asset.DisplayName,
            Shader = ReadPPtr(workspace, asset.FileInstance, bf["m_Shader"]),
            Keywords = ReadKeywords(bf),
            LightmapFlags = TryGetInt(bf, "m_LightmapFlags"),
            EnableInstancingVariants = TryGetBool(bf, "m_EnableInstancingVariants"),
            DoubleSidedGI = TryGetBool(bf, "m_DoubleSidedGI"),
            CustomRenderQueue = TryGetInt(bf, "m_CustomRenderQueue"),
            StringTags = ReadStringTagMap(bf),
            SavedProperties = ReadSavedProperties(workspace, asset.FileInstance, bf["m_SavedProperties"])
        };

        return data;
    }

    public static string FormatPreview(Workspace workspace, AssetInst asset, AssetTypeValueField bf)
    {
        var data = ReadMaterial(workspace, asset, bf);
        var sb = new StringBuilder();

        sb.AppendLine($"Type: {asset.Type}");
        sb.AppendLine($"File: {asset.FileName}");
        sb.AppendLine($"Path ID: {asset.PathId}");
        sb.AppendLine();
        sb.AppendLine($"Name: {data.Name}");

        AppendPPtrLine(sb, "Shader", data.Shader);
        if (data.Keywords.Count > 0)
            sb.AppendLine($"Keywords: {string.Join(", ", data.Keywords)}");

        if (data.LightmapFlags is not null)
            sb.AppendLine($"Lightmap Flags: {data.LightmapFlags}");
        if (data.EnableInstancingVariants is not null)
            sb.AppendLine($"Instancing Variants: {data.EnableInstancingVariants}");
        if (data.DoubleSidedGI is not null)
            sb.AppendLine($"Double Sided GI: {data.DoubleSidedGI}");
        if (data.CustomRenderQueue is not null)
            sb.AppendLine($"Custom Render Queue: {data.CustomRenderQueue}");

        if (data.StringTags.Count > 0)
        {
            sb.AppendLine("String Tags:");
            foreach (var (key, value) in data.StringTags)
                sb.AppendLine($"  {key} = {value}");
        }

        sb.AppendLine();
        AppendSavedPropertiesPreview(sb, data.SavedProperties);
        sb.AppendLine();
        sb.AppendLine("Use Plugins → Export Material / Import Material to edit properties via JSON.");
        return sb.ToString().TrimEnd();
    }

    public static string ExportToJson(Workspace workspace, AssetInst asset, AssetTypeValueField bf) =>
        JsonSerializer.Serialize(ReadMaterial(workspace, asset, bf), JsonOptions);

    public static void ImportFromJson(Workspace workspace, AssetInst asset, AssetTypeValueField bf, string json)
    {
        var data = JsonSerializer.Deserialize<MaterialExportData>(json, JsonOptions)
            ?? throw new InvalidDataException("Material JSON is empty or invalid.");

        if (!string.Equals(data.Format, "uabea-material", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Unsupported material format '{data.Format}'.");

        if (!string.IsNullOrEmpty(data.Name))
            SetString(bf, "m_Name", data.Name);

        if (data.Shader.PathId != 0 || data.Shader.FileId != 0)
            WritePPtr(bf["m_Shader"], data.Shader.FileId, data.Shader.PathId);

        WriteKeywords(bf, data.Keywords);

        if (data.LightmapFlags is not null)
            SetInt(bf, "m_LightmapFlags", data.LightmapFlags.Value);
        if (data.EnableInstancingVariants is not null)
            SetBool(bf, "m_EnableInstancingVariants", data.EnableInstancingVariants.Value);
        if (data.DoubleSidedGI is not null)
            SetBool(bf, "m_DoubleSidedGI", data.DoubleSidedGI.Value);
        if (data.CustomRenderQueue is not null)
            SetInt(bf, "m_CustomRenderQueue", data.CustomRenderQueue.Value);

        WriteStringTagMap(bf, data.StringTags);
        WriteSavedProperties(bf["m_SavedProperties"], data.SavedProperties);
    }

    private static MaterialSavedPropertiesData ReadSavedProperties(
        Workspace workspace, AssetsFileInstance fileInst, AssetTypeValueField savedProps)
    {
        var result = new MaterialSavedPropertiesData();
        if (savedProps.IsDummy)
            return result;

        var texEnvs = savedProps["m_TexEnvs.Array"];
        if (!texEnvs.IsDummy)
        {
            foreach (var entry in texEnvs.Children)
            {
                var name = TryGetPairFirstString(entry);
                if (name is null)
                    continue;

                var texData = entry["second"];
                result.Textures.Add(new MaterialTexEnvData
                {
                    Name = name,
                    Texture = ReadPPtr(workspace, fileInst, texData["m_Texture"]),
                    Scale = ReadVector2(texData["m_Scale"], 1f, 1f),
                    Offset = ReadVector2(texData["m_Offset"], 0f, 0f)
                });
            }
        }

        ReadPairArray(savedProps["m_Floats.Array"], result.Floats, entry =>
        {
            var name = TryGetPairFirstString(entry);
            if (name is null)
                return null;

            var second = entry["second"];
            if (second.IsDummy)
                return null;

            return new MaterialFloatData
            {
                Name = name,
                Value = second.AsFloat
            };
        });

        ReadPairArray(savedProps["m_Colors.Array"], result.Colors, entry =>
        {
            var name = TryGetPairFirstString(entry);
            if (name is null)
                return null;

            return new MaterialColorData
            {
                Name = name,
                Value = ReadColor(entry["second"])
            };
        });

        ReadPairArray(savedProps["m_Ints.Array"], result.Ints, entry =>
        {
            var name = TryGetPairFirstString(entry);
            if (name is null)
                return null;

            var second = entry["second"];
            if (second.IsDummy)
                return null;

            return new MaterialIntData
            {
                Name = name,
                Value = second.AsInt
            };
        });

        return result;
    }

    private static void WriteSavedProperties(AssetTypeValueField savedProps, MaterialSavedPropertiesData data)
    {
        if (savedProps.IsDummy)
            return;

        UpdatePairArray(savedProps["m_TexEnvs.Array"], data.Textures, (entry, tex) =>
        {
            if (!TrySetPairFirstString(entry, tex.Name))
                return false;

            var texData = entry["second"];
            if (texData.IsDummy)
                return false;

            WritePPtr(texData["m_Texture"], tex.Texture.FileId, tex.Texture.PathId);
            WriteVector2(texData["m_Scale"], tex.Scale);
            WriteVector2(texData["m_Offset"], tex.Offset);
            return true;
        });

        UpdatePairArray(savedProps["m_Floats.Array"], data.Floats, (entry, item) =>
        {
            if (!TrySetPairFirstString(entry, item.Name))
                return false;

            if (!entry["second"].IsDummy)
                entry["second"].AsFloat = item.Value;
            return true;
        });

        UpdatePairArray(savedProps["m_Colors.Array"], data.Colors, (entry, item) =>
        {
            if (!TrySetPairFirstString(entry, item.Name))
                return false;

            WriteColor(entry["second"], item.Value);
            return true;
        });

        UpdatePairArray(savedProps["m_Ints.Array"], data.Ints, (entry, item) =>
        {
            if (!TrySetPairFirstString(entry, item.Name))
                return false;

            if (!entry["second"].IsDummy)
                entry["second"].AsInt = item.Value;
            return true;
        });
    }

    private static void AppendSavedPropertiesPreview(StringBuilder sb, MaterialSavedPropertiesData props)
    {
        if (props.Textures.Count > 0)
        {
            sb.AppendLine($"Textures ({props.Textures.Count}):");
            foreach (var tex in props.Textures)
            {
                sb.AppendLine($"  {tex.Name}");
                AppendPPtrLine(sb, "    Texture", tex.Texture, indent: "    ");
                sb.AppendLine($"    Scale: {FormatVector2(tex.Scale)}");
                sb.AppendLine($"    Offset: {FormatVector2(tex.Offset)}");
            }
        }

        if (props.Floats.Count > 0)
        {
            sb.AppendLine($"Floats ({props.Floats.Count}):");
            foreach (var item in props.Floats)
                sb.AppendLine($"  {item.Name} = {item.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        if (props.Colors.Count > 0)
        {
            sb.AppendLine($"Colors ({props.Colors.Count}):");
            foreach (var item in props.Colors)
                sb.AppendLine($"  {item.Name} = {FormatColor(item.Value)}");
        }

        if (props.Ints.Count > 0)
        {
            sb.AppendLine($"Ints ({props.Ints.Count}):");
            foreach (var item in props.Ints)
                sb.AppendLine($"  {item.Name} = {item.Value}");
        }
    }

    private static MaterialPPtrData ReadPPtr(Workspace workspace, AssetsFileInstance fileInst, AssetTypeValueField pptrField)
    {
        var data = new MaterialPPtrData();
        if (pptrField.IsDummy)
            return data;

        if (!pptrField["m_FileID"].IsDummy)
            data.FileId = pptrField["m_FileID"].AsInt;
        if (!pptrField["m_PathID"].IsDummy)
            data.PathId = pptrField["m_PathID"].AsLong;

        var target = workspace.GetAssetInst(fileInst, pptrField);
        if (target is not null)
            data.Name = target.DisplayName;

        return data;
    }

    private static void WritePPtr(AssetTypeValueField pptrField, int fileId, long pathId)
    {
        if (pptrField.IsDummy)
            return;

        if (!pptrField["m_FileID"].IsDummy)
            pptrField["m_FileID"].AsInt = fileId;
        if (!pptrField["m_PathID"].IsDummy)
            pptrField["m_PathID"].AsLong = pathId;
    }

    private static List<string> ReadKeywords(AssetTypeValueField bf)
    {
        var keywords = ReadStringArray(bf["m_ShaderKeywords.Array"]);
        if (keywords.Count > 0)
            return keywords;

        keywords = ReadStringArray(bf["m_ValidKeywords.Array"]);
        return keywords;
    }

    private static void WriteKeywords(AssetTypeValueField bf, List<string> keywords)
    {
        if (keywords.Count == 0)
            return;

        if (!bf["m_ShaderKeywords.Array"].IsDummy)
        {
            WriteStringArray(bf["m_ShaderKeywords.Array"], keywords);
            return;
        }

        if (!bf["m_ValidKeywords.Array"].IsDummy)
            WriteStringArray(bf["m_ValidKeywords.Array"], keywords);
    }

    private static Dictionary<string, string> ReadStringTagMap(AssetTypeValueField bf)
    {
        var result = new Dictionary<string, string>();
        var tagMap = bf["stringTagMap.Array"];
        if (tagMap.IsDummy)
            return result;

        foreach (var entry in tagMap.Children)
        {
            var key = TryGetPairFirstString(entry);
            if (key is null)
                continue;

            var valueField = entry["second"];
            if (!valueField.IsDummy)
                result[key] = valueField.AsString;
        }

        return result;
    }

    private static void WriteStringTagMap(AssetTypeValueField bf, Dictionary<string, string> tags)
    {
        if (tags.Count == 0)
            return;

        var tagMap = bf["stringTagMap.Array"];
        if (tagMap.IsDummy)
            return;

        foreach (var entry in tagMap.Children)
        {
            var key = TryGetPairFirstString(entry);
            if (key is null || !tags.TryGetValue(key, out var value))
                continue;

            if (!entry["second"].IsDummy)
                entry["second"].AsString = value;
        }
    }

    private static List<string> ReadStringArray(AssetTypeValueField arrayField)
    {
        var result = new List<string>();
        if (arrayField.IsDummy)
            return result;

        foreach (var entry in arrayField.Children)
            result.Add(entry.AsString);

        return result;
    }

    private static void WriteStringArray(AssetTypeValueField arrayField, List<string> values)
    {
        if (arrayField.IsDummy)
            return;

        var i = 0;
        foreach (var entry in arrayField.Children)
        {
            if (i >= values.Count)
                break;

            entry.AsString = values[i];
            i++;
        }
    }

    private static string? TryGetPairFirstString(AssetTypeValueField pairField)
    {
        if (pairField.IsDummy)
            return null;

        var first = pairField["first"];
        return first.IsDummy ? null : first.AsString;
    }

    private static bool TrySetPairFirstString(AssetTypeValueField pairField, string value)
    {
        if (pairField.IsDummy)
            return false;

        var first = pairField["first"];
        if (first.IsDummy)
            return false;

        first.AsString = value;
        return true;
    }

    private static void ReadPairArray<T>(AssetTypeValueField arrayField, List<T> target, Func<AssetTypeValueField, T?> selector)
        where T : class
    {
        if (arrayField.IsDummy)
            return;

        foreach (var entry in arrayField.Children)
        {
            var item = selector(entry);
            if (item is not null)
                target.Add(item);
        }
    }

    private static void UpdatePairArray<T>(AssetTypeValueField arrayField, List<T> source, Func<AssetTypeValueField, T, bool> updater)
    {
        if (arrayField.IsDummy || source.Count == 0)
            return;

        foreach (var entry in arrayField.Children)
        {
            var name = TryGetPairFirstString(entry);
            if (name is null)
                continue;

            var item = source.FirstOrDefault(x => GetPropertyName(x) == name);
            if (item is null)
                continue;

            updater(entry, item);
        }
    }

    private static string GetPropertyName<T>(T item) => item switch
    {
        MaterialTexEnvData tex => tex.Name,
        MaterialFloatData fl => fl.Name,
        MaterialColorData col => col.Name,
        MaterialIntData integer => integer.Name,
        _ => string.Empty
    };

    private static float[] ReadVector2(AssetTypeValueField field, float defaultX, float defaultY)
    {
        if (field.IsDummy)
            return [defaultX, defaultY];

        var x = field["x"].IsDummy ? defaultX : field["x"].AsFloat;
        var y = field["y"].IsDummy ? defaultY : field["y"].AsFloat;
        return [x, y];
    }

    private static void WriteVector2(AssetTypeValueField field, float[] values)
    {
        if (field.IsDummy || values.Length < 2)
            return;

        if (!field["x"].IsDummy)
            field["x"].AsFloat = values[0];
        if (!field["y"].IsDummy)
            field["y"].AsFloat = values[1];
    }

    private static float[] ReadColor(AssetTypeValueField field)
    {
        if (field.IsDummy)
            return [1f, 1f, 1f, 1f];

        return
        [
            field["r"].IsDummy ? 1f : field["r"].AsFloat,
            field["g"].IsDummy ? 1f : field["g"].AsFloat,
            field["b"].IsDummy ? 1f : field["b"].AsFloat,
            field["a"].IsDummy ? 1f : field["a"].AsFloat
        ];
    }

    private static void WriteColor(AssetTypeValueField field, float[] values)
    {
        if (field.IsDummy || values.Length < 4)
            return;

        if (!field["r"].IsDummy) field["r"].AsFloat = values[0];
        if (!field["g"].IsDummy) field["g"].AsFloat = values[1];
        if (!field["b"].IsDummy) field["b"].AsFloat = values[2];
        if (!field["a"].IsDummy) field["a"].AsFloat = values[3];
    }

    private static string? TryGetString(AssetTypeValueField bf, string fieldName)
    {
        var field = bf[fieldName];
        return field.IsDummy ? null : field.AsString;
    }

    private static int? TryGetInt(AssetTypeValueField bf, string fieldName)
    {
        var field = bf[fieldName];
        return field.IsDummy ? null : field.AsInt;
    }

    private static bool? TryGetBool(AssetTypeValueField bf, string fieldName)
    {
        var field = bf[fieldName];
        return field.IsDummy ? null : field.AsBool;
    }

    private static void SetString(AssetTypeValueField bf, string fieldName, string value)
    {
        var field = bf[fieldName];
        if (!field.IsDummy)
            field.AsString = value;
    }

    private static void SetInt(AssetTypeValueField bf, string fieldName, int value)
    {
        var field = bf[fieldName];
        if (!field.IsDummy)
            field.AsInt = value;
    }

    private static void SetBool(AssetTypeValueField bf, string fieldName, bool value)
    {
        var field = bf[fieldName];
        if (!field.IsDummy)
            field.AsBool = value;
    }

    private static void AppendPPtrLine(StringBuilder sb, string label, MaterialPPtrData pptr, string indent = "")
    {
        sb.AppendLine($"{indent}{label}: fileId={pptr.FileId} pathId={pptr.PathId}");
        if (!string.IsNullOrEmpty(pptr.Name))
            sb.AppendLine($"{indent}  -> {pptr.Name}");
    }

    private static string FormatVector2(float[] values) =>
        values.Length >= 2
            ? $"{values[0].ToString(CultureInfo.InvariantCulture)}, {values[1].ToString(CultureInfo.InvariantCulture)}"
            : "0, 0";

    private static string FormatColor(float[] values) =>
        values.Length >= 4
            ? $"({values[0]:F3}, {values[1]:F3}, {values[2]:F3}, {values[3]:F3})"
            : "(1, 1, 1, 1)";
}