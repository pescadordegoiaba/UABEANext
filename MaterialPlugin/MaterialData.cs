namespace MaterialPlugin;

public sealed class MaterialPPtrData
{
    public int FileId { get; set; }
    public long PathId { get; set; }
    public string? Name { get; set; }
}

public sealed class MaterialTexEnvData
{
    public string Name { get; set; } = string.Empty;
    public MaterialPPtrData Texture { get; set; } = new();
    public float[] Scale { get; set; } = [1f, 1f];
    public float[] Offset { get; set; } = [0f, 0f];
}

public sealed class MaterialFloatData
{
    public string Name { get; set; } = string.Empty;
    public float Value { get; set; }
}

public sealed class MaterialColorData
{
    public string Name { get; set; } = string.Empty;
    public float[] Value { get; set; } = [1f, 1f, 1f, 1f];
}

public sealed class MaterialIntData
{
    public string Name { get; set; } = string.Empty;
    public int Value { get; set; }
}

public sealed class MaterialSavedPropertiesData
{
    public List<MaterialTexEnvData> Textures { get; set; } = [];
    public List<MaterialFloatData> Floats { get; set; } = [];
    public List<MaterialColorData> Colors { get; set; } = [];
    public List<MaterialIntData> Ints { get; set; } = [];
}

public sealed class MaterialExportData
{
    public string Format { get; set; } = "uabea-material";
    public int Version { get; set; } = 1;
    public string Name { get; set; } = string.Empty;
    public MaterialPPtrData Shader { get; set; } = new();
    public List<string> Keywords { get; set; } = [];
    public int? LightmapFlags { get; set; }
    public bool? EnableInstancingVariants { get; set; }
    public bool? DoubleSidedGI { get; set; }
    public int? CustomRenderQueue { get; set; }
    public Dictionary<string, string> StringTags { get; set; } = [];
    public MaterialSavedPropertiesData SavedProperties { get; set; } = new();
}