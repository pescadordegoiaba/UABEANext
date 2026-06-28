using System.Numerics;

namespace UABEANext4.Logic.Mesh;

/// <summary>GPU-ready mesh data for the OpenGL preview (centered like AssetStudio).</summary>
public sealed class MeshPreviewRenderData
{
    public MeshPreviewVertex[] Vertices { get; init; } = [];
    public uint[] TriangleIndices { get; init; } = [];
    public uint[] WireframeIndices { get; init; } = [];
    public Matrix4x4 ModelMatrix { get; init; } = Matrix4x4.Identity;
    public int VertexCount { get; init; }
    public int TriangleCount { get; init; }
    public Vector3 BoundsMin { get; init; }
    public Vector3 BoundsMax { get; init; }
    public bool HasVertexColors { get; init; }
    public bool HasAssetNormals { get; init; }
}

public struct MeshPreviewVertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector3 CalculatedNormal;
    public Vector4 Color;
}