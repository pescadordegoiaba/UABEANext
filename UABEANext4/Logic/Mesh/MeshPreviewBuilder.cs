using System;
using System.Collections.Generic;
using System.Numerics;

namespace UABEANext4.Logic.Mesh;

/// <summary>
/// Prepares <see cref="MeshObj"/> for 3D preview (centering/scaling and normals), matching AssetStudio behaviour.
/// </summary>
public static class MeshPreviewBuilder
{
    public static MeshPreviewRenderData? Build(MeshObj mesh)
    {
        if (mesh.VertexCount <= 0 || mesh.Vertices.Length < mesh.VertexCount * 3)
            return null;

        if (mesh.Indices.Length < 3)
            return null;

        var vertexCount = mesh.VertexCount;
        var vertexStride = mesh.Vertices.Length >= vertexCount * 4 ? 4 : 3;
        var normalStride = mesh.Normals.Length >= vertexCount * 3
            ? (mesh.Normals.Length >= vertexCount * 4 ? 4 : 3)
            : 0;

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var positions = new Vector3[vertexCount];

        for (var i = 0; i < vertexCount; i++)
        {
            var p = new Vector3(
                mesh.Vertices[i * vertexStride],
                mesh.Vertices[i * vertexStride + 1],
                mesh.Vertices[i * vertexStride + 2]);
            positions[i] = p;
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        var offset = (min + max) * 0.5f;
        var dist = max - min;
        var d = MathF.Max(1e-5f, dist.Length());
        var modelMatrix = Matrix4x4.CreateTranslation(-offset) * Matrix4x4.CreateScale(2f / d);

        var calcNormals = CalculateNormals(positions, mesh.Indices);
        var hasAssetNormals = normalStride >= 3;
        var hasVertexColors = TryGetVertexColors(mesh, vertexCount, out var colors);

        var vertices = new MeshPreviewVertex[vertexCount];
        for (var i = 0; i < vertexCount; i++)
        {
            Vector3 assetNormal = Vector3.UnitY;
            if (hasAssetNormals)
            {
                assetNormal = new Vector3(
                    mesh.Normals[i * normalStride],
                    mesh.Normals[i * normalStride + 1],
                    mesh.Normals[i * normalStride + 2]);
                if (assetNormal.LengthSquared() > 1e-8f)
                    assetNormal = Vector3.Normalize(assetNormal);
            }

            var color = hasVertexColors
                ? colors[i]
                : new Vector4(0.5f, 0.5f, 0.5f, 1f);

            vertices[i] = new MeshPreviewVertex
            {
                Position = positions[i],
                Normal = assetNormal,
                CalculatedNormal = calcNormals[i],
                Color = color
            };
        }

        var wireIndices = BuildWireframeIndices(mesh.Indices);

        return new MeshPreviewRenderData
        {
            Vertices = vertices,
            TriangleIndices = mesh.Indices,
            WireframeIndices = wireIndices,
            ModelMatrix = modelMatrix,
            VertexCount = vertexCount,
            TriangleCount = mesh.Indices.Length / 3,
            BoundsMin = min,
            BoundsMax = max,
            HasVertexColors = hasVertexColors,
            HasAssetNormals = hasAssetNormals
        };
    }

    private static bool TryGetVertexColors(MeshObj mesh, int vertexCount, out Vector4[] colors)
    {
        colors = [];
        var c = mesh.Colors;
        if (c.Length == vertexCount * 3)
        {
            colors = new Vector4[vertexCount];
            for (var i = 0; i < vertexCount; i++)
            {
                colors[i] = new Vector4(c[i * 3], c[i * 3 + 1], c[i * 3 + 2], 1f);
            }
            return true;
        }

        if (c.Length == vertexCount * 4)
        {
            colors = new Vector4[vertexCount];
            for (var i = 0; i < vertexCount; i++)
            {
                colors[i] = new Vector4(c[i * 4], c[i * 4 + 1], c[i * 4 + 2], c[i * 4 + 3]);
            }
            return true;
        }

        return false;
    }

    private static Vector3[] CalculateNormals(Vector3[] positions, uint[] indices)
    {
        var normals = new Vector3[positions.Length];
        var counts = new int[positions.Length];

        for (var i = 0; i + 2 < indices.Length; i += 3)
        {
            var ia = (int)indices[i];
            var ib = (int)indices[i + 1];
            var ic = (int)indices[i + 2];
            if (ia < 0 || ib < 0 || ic < 0 || ia >= positions.Length || ib >= positions.Length || ic >= positions.Length)
                continue;

            var dir1 = positions[ib] - positions[ia];
            var dir2 = positions[ic] - positions[ia];
            var n = Vector3.Cross(dir1, dir2);
            if (n.LengthSquared() < 1e-12f)
                continue;
            n = Vector3.Normalize(n);

            normals[ia] += n;
            normals[ib] += n;
            normals[ic] += n;
            counts[ia]++;
            counts[ib]++;
            counts[ic]++;
        }

        for (var v = 0; v < positions.Length; v++)
        {
            if (counts[v] == 0)
                normals[v] = Vector3.UnitY;
            else
            {
                normals[v] /= counts[v];
                if (normals[v].LengthSquared() > 1e-8f)
                    normals[v] = Vector3.Normalize(normals[v]);
                else
                    normals[v] = Vector3.UnitY;
            }
        }

        return normals;
    }

    private static uint[] BuildWireframeIndices(uint[] triangleIndices)
    {
        var edges = new HashSet<ulong>();
        var lines = new List<uint>(triangleIndices.Length * 2);

        static ulong EdgeKey(uint a, uint b)
        {
            if (a > b)
                (a, b) = (b, a);
            return ((ulong)a << 32) | b;
        }

        for (var i = 0; i + 2 < triangleIndices.Length; i += 3)
        {
            var a = triangleIndices[i];
            var b = triangleIndices[i + 1];
            var c = triangleIndices[i + 2];

            foreach (var (x, y) in new[] { (a, b), (b, c), (c, a) })
            {
                var key = EdgeKey(x, y);
                if (edges.Add(key))
                {
                    lines.Add(x);
                    lines.Add(y);
                }
            }
        }

        return lines.ToArray();
    }
}