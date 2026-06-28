namespace UABEANext4.Logic.Mesh;

public sealed class ImportedMeshGeometry
{
    public float[] Vertices { get; init; } = [];
    public float[] Normals { get; init; } = [];
    public float[] Uvs { get; init; } = [];
    public uint[] Indices { get; init; } = [];

    public int VertexCount => Vertices.Length / 3;
    public int TriangleCount => Indices.Length / 3;
}