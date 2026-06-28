using System.Globalization;
using System.Text;
using UABEANext4.Logic.Mesh;

namespace MeshPlugin;

internal static class MeshObjExporter
{
    public static void Export(string filePath, MeshObj mesh, string objectName)
    {
        using var writer = new StreamWriter(filePath, false, new UTF8Encoding(false));
        WriteObj(writer, mesh, objectName);
    }

    private static void WriteObj(TextWriter writer, MeshObj mesh, string objectName)
    {
        var vertexCount = mesh.VertexCount;
        if (vertexCount == 0)
        {
            throw new InvalidDataException("Mesh has no vertices.");
        }

        var vertexStride = mesh.Vertices.Length >= vertexCount * 3 ? mesh.Vertices.Length / vertexCount : 0;
        if (vertexStride < 3)
        {
            throw new InvalidDataException("Mesh vertex channel is missing or invalid.");
        }

        var normalStride = mesh.Normals.Length >= vertexCount * 3 ? mesh.Normals.Length / vertexCount : 0;
        var uv0 = mesh.UVs.Length > 0 ? mesh.UVs[0] : null;
        var uvStride = uv0 is not null && uv0.Length >= vertexCount * 2 ? uv0.Length / vertexCount : 0;

        writer.WriteLine("# Exported by UABEANext MeshPlugin");
        writer.WriteLine("# UABEA-NEXT-UNITY-WINDING");
        writer.Write("o ");
        writer.WriteLine(SanitizeObjName(objectName));

        for (var i = 0; i < vertexCount; i++)
        {
            var offset = i * vertexStride;
            WriteObjTuple(writer, "v", -mesh.Vertices[offset], mesh.Vertices[offset + 1], mesh.Vertices[offset + 2]);
        }

        if (uv0 is not null && uvStride >= 2)
        {
            for (var i = 0; i < vertexCount; i++)
            {
                var offset = i * uvStride;
                WriteObjTuple(writer, "vt", uv0[offset], uv0[offset + 1]);
            }
        }

        if (normalStride >= 3)
        {
            for (var i = 0; i < vertexCount; i++)
            {
                var offset = i * normalStride;
                WriteObjTuple(writer, "vn", -mesh.Normals[offset], mesh.Normals[offset + 1], mesh.Normals[offset + 2]);
            }
        }

        writer.WriteLine("s 1");
        for (var i = 0; i + 2 < mesh.Indices.Length; i += 3)
        {
            var a = CheckedObjIndex(mesh.Indices[i], vertexCount);
            var b = CheckedObjIndex(mesh.Indices[i + 1], vertexCount);
            var c = CheckedObjIndex(mesh.Indices[i + 2], vertexCount);

            writer.Write("f ");
            WriteFaceIndex(writer, a, uvStride >= 2, normalStride >= 3);
            writer.Write(' ');
            WriteFaceIndex(writer, b, uvStride >= 2, normalStride >= 3);
            writer.Write(' ');
            WriteFaceIndex(writer, c, uvStride >= 2, normalStride >= 3);
            writer.WriteLine();
        }
    }

    private static int CheckedObjIndex(uint index, int vertexCount)
    {
        if (index >= (uint)vertexCount)
        {
            throw new InvalidDataException($"Mesh index {index} is outside the vertex range 0-{vertexCount - 1}.");
        }

        return checked((int)index + 1);
    }

    private static void WriteFaceIndex(TextWriter writer, int index, bool hasUv, bool hasNormal)
    {
        writer.Write(index.ToString(CultureInfo.InvariantCulture));
        if (hasUv)
        {
            writer.Write('/');
            writer.Write(index.ToString(CultureInfo.InvariantCulture));
            if (hasNormal)
            {
                writer.Write('/');
                writer.Write(index.ToString(CultureInfo.InvariantCulture));
            }
        }
        else if (hasNormal)
        {
            writer.Write("//");
            writer.Write(index.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void WriteObjTuple(TextWriter writer, string prefix, params float[] values)
    {
        writer.Write(prefix);
        foreach (var value in values)
        {
            writer.Write(' ');
            writer.Write(value.ToString("R", CultureInfo.InvariantCulture));
        }
        writer.WriteLine();
    }

    private static string SanitizeObjName(string name)
    {
        var sanitized = name.Trim();
        if (sanitized.Length == 0)
        {
            return "Mesh";
        }

        return sanitized.Replace(' ', '_').Replace('\t', '_');
    }
}
