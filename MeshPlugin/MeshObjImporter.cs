using System.Globalization;
using System.Numerics;
using UABEANext4.Logic.Mesh;

namespace MeshPlugin;

internal static class MeshObjImporter
{
    private readonly record struct ObjCorner(int V, int Vt, int Vn);

    public static ImportedMeshGeometry ReadMesh(string filePath)
    {
        var positions = new List<Vector3>();
        var uvs = new List<Vector2>();
        var normals = new List<Vector3>();
        var faces = new List<ObjCorner[]>();
        var legacyUabeaExport = false;
        var unityWindingExport = false;

        foreach (var rawLine in File.ReadLines(filePath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            if (line[0] == '#')
            {
                if (line.Contains("Exported by UABEANext MeshPlugin", StringComparison.Ordinal))
                {
                    legacyUabeaExport = true;
                }

                if (line.Contains("UABEA-NEXT-UNITY-WINDING", StringComparison.Ordinal))
                {
                    unityWindingExport = true;
                }

                continue;
            }

            if (line.StartsWith("v ", StringComparison.Ordinal))
            {
                positions.Add(ParseVertex(line));
                continue;
            }

            if (line.StartsWith("vt ", StringComparison.Ordinal))
            {
                uvs.Add(ParseUv(line));
                continue;
            }

            if (line.StartsWith("vn ", StringComparison.Ordinal))
            {
                normals.Add(ParseNormal(line));
                continue;
            }

            if (line.StartsWith("f ", StringComparison.Ordinal))
            {
                faces.Add(ParseFace(line));
            }
        }

        if (positions.Count == 0)
            throw new InvalidDataException("OBJ contains no vertex positions.");

        if (faces.Count == 0)
            throw new InvalidDataException("OBJ contains no faces.");

        var windingMode = ResolveWindingMode(legacyUabeaExport, unityWindingExport);
        return BuildGeometry(positions, uvs, normals, faces, windingMode);
    }

    private enum ObjWindingMode
    {
        FlipForHandedness,
        LegacyUabeaReverse,
        PreserveUnity
    }

    private static ObjWindingMode ResolveWindingMode(bool legacyUabeaExport, bool unityWindingExport)
    {
        if (unityWindingExport)
        {
            return ObjWindingMode.PreserveUnity;
        }

        if (legacyUabeaExport)
        {
            return ObjWindingMode.LegacyUabeaReverse;
        }

        return ObjWindingMode.FlipForHandedness;
    }

    public static List<float> ReadVertexPositions(string filePath)
    {
        var geometry = ReadMesh(filePath);
        return geometry.Vertices.ToList();
    }

    private static ImportedMeshGeometry BuildGeometry(
        List<Vector3> positions,
        List<Vector2> uvs,
        List<Vector3> normals,
        List<ObjCorner[]> faces,
        ObjWindingMode windingMode)
    {
        var weldedVertices = new List<Vector3>();
        var weldedNormals = new List<Vector3>();
        var weldedUvs = new List<Vector2>();
        var indices = new List<uint>();
        var cornerMap = new Dictionary<ObjCorner, int>();

        foreach (var face in faces)
        {
            if (face.Length < 3)
                continue;

            for (var tri = 0; tri < face.Length - 2; tri++)
            {
                var corners = new[] { face[0], face[tri + 1], face[tri + 2] };
                foreach (var corner in corners)
                {
                    if (!cornerMap.TryGetValue(corner, out var index))
                    {
                        index = weldedVertices.Count;
                        cornerMap[corner] = index;
                        weldedVertices.Add(ResolvePosition(positions, corner.V));
                        weldedNormals.Add(ResolveNormal(normals, corner.Vn, corner.V, positions));
                        weldedUvs.Add(ResolveUv(uvs, corner.Vt));
                    }

                    indices.Add((uint)index);
                }
            }
        }

        if (indices.Count < 3)
            throw new InvalidDataException("OBJ contains no triangulated faces.");

        switch (windingMode)
        {
            case ObjWindingMode.LegacyUabeaReverse:
                ReverseTriangleWinding(indices);
                break;
            case ObjWindingMode.FlipForHandedness:
                // Negating X mirrors the mesh; swap two corners per triangle for Unity's left-handed space.
                FlipTriangleWindingForUnity(indices);
                break;
            case ObjWindingMode.PreserveUnity:
                break;
        }

        var vertexCount = weldedVertices.Count;
        var vertexArray = new float[vertexCount * 3];
        var normalArray = new float[vertexCount * 3];
        var uvArray = new float[vertexCount * 2];

        for (var i = 0; i < vertexCount; i++)
        {
            vertexArray[i * 3] = weldedVertices[i].X;
            vertexArray[i * 3 + 1] = weldedVertices[i].Y;
            vertexArray[i * 3 + 2] = weldedVertices[i].Z;

            normalArray[i * 3] = weldedNormals[i].X;
            normalArray[i * 3 + 1] = weldedNormals[i].Y;
            normalArray[i * 3 + 2] = weldedNormals[i].Z;

            uvArray[i * 2] = weldedUvs[i].X;
            uvArray[i * 2 + 1] = weldedUvs[i].Y;
        }

        normalArray = CalculateNormals(vertexArray, indices);

        return new ImportedMeshGeometry
        {
            Vertices = vertexArray,
            Normals = normalArray,
            Uvs = uvArray,
            Indices = indices.ToArray()
        };
    }

    private static Vector3 ParseVertex(string line)
    {
        var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
            throw new InvalidDataException("OBJ contains a vertex line with fewer than three coordinates.");

        var x = ParseFloat(parts[1]);
        var y = ParseFloat(parts[2]);
        var z = ParseFloat(parts[3]);
        return new Vector3(-x, y, z);
    }

    private static Vector2 ParseUv(string line)
    {
        var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
            throw new InvalidDataException("OBJ contains a UV line with fewer than two coordinates.");

        return new Vector2(ParseFloat(parts[1]), ParseFloat(parts[2]));
    }

    private static Vector3 ParseNormal(string line)
    {
        var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
            throw new InvalidDataException("OBJ contains a normal line with fewer than three coordinates.");

        var x = ParseFloat(parts[1]);
        var y = ParseFloat(parts[2]);
        var z = ParseFloat(parts[3]);
        return Vector3.Normalize(new Vector3(-x, y, z));
    }

    private static ObjCorner[] ParseFace(string line)
    {
        var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
            throw new InvalidDataException("OBJ face must have at least three corners.");

        var corners = new ObjCorner[parts.Length - 1];
        for (var i = 1; i < parts.Length; i++)
        {
            corners[i - 1] = ParseCorner(parts[i]);
        }

        return corners;
    }

    private static ObjCorner ParseCorner(string token)
    {
        var chunks = token.Split('/');
        var v = ParseIndex(chunks, 0);
        var vt = chunks.Length > 1 && chunks[1].Length > 0 ? ParseIndex(chunks, 1) : -1;
        var vn = chunks.Length > 2 && chunks[2].Length > 0 ? ParseIndex(chunks, 2) : -1;
        return new ObjCorner(v, vt, vn);
    }

    private static int ParseIndex(string[] chunks, int chunkIndex)
    {
        var text = chunks[chunkIndex];
        var index = int.Parse(text, CultureInfo.InvariantCulture);
        return index;
    }

    private static Vector3 ResolvePosition(List<Vector3> positions, int index)
    {
        var resolved = ResolveListIndex(positions.Count, index);
        return positions[resolved];
    }

    private static Vector3 ResolveNormal(List<Vector3> normals, int normalIndex, int vertexIndex, List<Vector3> positions)
    {
        if (normalIndex > 0 && normals.Count > 0)
        {
            return normals[ResolveListIndex(normals.Count, normalIndex)];
        }

        if (normalIndex < 0 && normals.Count > 0 && vertexIndex > 0)
        {
            return normals[ResolveListIndex(normals.Count, vertexIndex)];
        }

        return Vector3.UnitY;
    }

    private static Vector2 ResolveUv(List<Vector2> uvs, int uvIndex)
    {
        if (uvIndex <= 0 || uvs.Count == 0)
            return Vector2.Zero;

        return uvs[ResolveListIndex(uvs.Count, uvIndex)];
    }

    private static int ResolveListIndex(int count, int index)
    {
        if (index > 0)
            return index - 1;

        return count + index;
    }

    private static void FlipTriangleWindingForUnity(List<uint> indices)
    {
        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
        }
    }

    private static void ReverseTriangleWinding(List<uint> indices)
    {
        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            (indices[i], indices[i + 2]) = (indices[i + 2], indices[i]);
        }
    }

    private static float[] CalculateNormals(float[] vertices, List<uint> indices)
    {
        var vertexCount = vertices.Length / 3;
        var normals = new Vector3[vertexCount];
        var counts = new int[vertexCount];

        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var ia = (int)indices[i];
            var ib = (int)indices[i + 1];
            var ic = (int)indices[i + 2];
            if (ia < 0 || ib < 0 || ic < 0 || ia >= vertexCount || ib >= vertexCount || ic >= vertexCount)
                continue;

            var a = new Vector3(vertices[ia * 3], vertices[ia * 3 + 1], vertices[ia * 3 + 2]);
            var b = new Vector3(vertices[ib * 3], vertices[ib * 3 + 1], vertices[ib * 3 + 2]);
            var c = new Vector3(vertices[ic * 3], vertices[ic * 3 + 1], vertices[ic * 3 + 2]);
            var n = Vector3.Cross(c - a, b - a);
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

        var result = new float[vertexCount * 3];
        for (var v = 0; v < vertexCount; v++)
        {
            var n = counts[v] == 0 ? Vector3.UnitY : Vector3.Normalize(normals[v] / counts[v]);
            result[v * 3] = n.X;
            result[v * 3 + 1] = n.Y;
            result[v * 3 + 2] = n.Z;
        }

        return result;
    }

    private static float ParseFloat(string value) =>
        float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
}