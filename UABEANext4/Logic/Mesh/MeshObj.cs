using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System.Buffers.Binary;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UABEANext4.Logic.Mesh;

// based on https://github.com/Perfare/AssetStudio/blob/master/AssetStudio/Classes/Mesh.cs
// this is not in a plugin due to being needed by the previewer, plus it's shared by multiple plugins
public class MeshObj
{
    public uint[] Indices;
    public List<Channel> Channels;
    public float[] Vertices;
    public float[] Normals;
    public float[] Tangents;
    public float[] Colors;
    public float[][] UVs;
    public int VertexCount;

    public MeshObj()
    {
        Indices = [];
        Channels = [];
        Vertices = [];
        Normals = [];
        Tangents = [];
        Colors = [];
        UVs = [];
        VertexCount = 0;
    }

    public MeshObj(AssetsFileInstance fileInst, AssetTypeValueField baseField, UnityVersion version)
    {
        Indices = [];
        Channels = [];
        Vertices = [];
        Normals = [];
        Tangents = [];
        Colors = [];
        UVs = [];
        VertexCount = 0;

        Read(fileInst, baseField, version);
    }

    private void Read(AssetsFileInstance fileInst, AssetTypeValueField baseField, UnityVersion version)
    {
        ReadIndicesData(baseField);
        ReadChannels(baseField);
        ReadVertexData(fileInst, baseField, version);
    }

    private void ReadIndicesData(AssetTypeValueField baseField)
    {
        var indicesField = baseField["m_IndexBuffer.Array"].AsByteArray;
        var use16BitIndices = true;
        var indexFormatField = baseField["m_IndexFormat"];
        if (!indexFormatField.IsDummy)
        {
            use16BitIndices = indexFormatField.AsInt == 0;
        }

        var indexSize = use16BitIndices ? 2 : 4;
        var indexBuffer = new uint[indicesField.Length / indexSize];
        for (var i = 0; i < indexBuffer.Length; i++)
        {
            var src = i * indexSize;
            indexBuffer[i] = use16BitIndices
                ? BinaryPrimitives.ReadUInt16LittleEndian(indicesField.AsSpan(src, 2))
                : BinaryPrimitives.ReadUInt32LittleEndian(indicesField.AsSpan(src, 4));
        }

        Indices = GetTriangleIndices(baseField, indexBuffer, indexSize);
    }

    private static uint[] GetTriangleIndices(AssetTypeValueField baseField, uint[] indexBuffer, int indexSize)
    {
        var subMeshes = baseField["m_SubMeshes.Array"];
        if (subMeshes.IsDummy || subMeshes.Children.Count == 0)
        {
            return indexBuffer;
        }

        var indices = new List<uint>(indexBuffer.Length);
        foreach (var subMesh in subMeshes)
        {
            var firstByteField = subMesh["firstByte"];
            var indexCountField = subMesh["indexCount"];
            var topologyField = subMesh["topology"];
            if (firstByteField.IsDummy || indexCountField.IsDummy || topologyField.IsDummy)
            {
                continue;
            }

            var firstIndex = checked((int)(firstByteField.AsUInt / indexSize));
            var indexCount = checked((int)indexCountField.AsUInt);
            var topology = (GfxPrimitiveType)topologyField.AsInt;

            if (firstIndex < 0 || firstIndex > indexBuffer.Length || firstIndex + indexCount > indexBuffer.Length)
            {
                throw new InvalidDataException("Mesh submesh index range is outside the index buffer.");
            }

            switch (topology)
            {
                case GfxPrimitiveType.Triangles:
                {
                    for (var i = 0; i + 2 < indexCount; i += 3)
                    {
                        indices.Add(indexBuffer[firstIndex + i]);
                        indices.Add(indexBuffer[firstIndex + i + 1]);
                        indices.Add(indexBuffer[firstIndex + i + 2]);
                    }
                    break;
                }
                case GfxPrimitiveType.TriangleStrip:
                {
                    var triIndex = 0;
                    for (var i = 0; i < indexCount - 2; i++)
                    {
                        var a = indexBuffer[firstIndex + i];
                        var b = indexBuffer[firstIndex + i + 1];
                        var c = indexBuffer[firstIndex + i + 2];

                        if (a == b || a == c || b == c)
                        {
                            continue;
                        }

                        indices.Add(a);
                        if ((triIndex & 1) == 0)
                        {
                            indices.Add(b);
                            indices.Add(c);
                        }
                        else
                        {
                            indices.Add(c);
                            indices.Add(b);
                        }
                        triIndex++;
                    }
                    break;
                }
                case GfxPrimitiveType.Quads:
                {
                    for (var i = 0; i + 3 < indexCount; i += 4)
                    {
                        var a = indexBuffer[firstIndex + i];
                        var b = indexBuffer[firstIndex + i + 1];
                        var c = indexBuffer[firstIndex + i + 2];
                        var d = indexBuffer[firstIndex + i + 3];

                        indices.Add(a);
                        indices.Add(b);
                        indices.Add(c);
                        indices.Add(a);
                        indices.Add(c);
                        indices.Add(d);
                    }
                    break;
                }
                case GfxPrimitiveType.Lines:
                case GfxPrimitiveType.LineStrip:
                case GfxPrimitiveType.Points:
                    break;
                default:
                    throw new InvalidDataException($"Unsupported mesh submesh topology {topology}.");
            }
        }

        return indices.ToArray();
    }

    private void ReadChannels(AssetTypeValueField baseField)
    {
        var channelFields = baseField["m_VertexData"]["m_Channels.Array"];
        var channels = new List<Channel>();
        foreach (var channelField in channelFields)
        {
            channels.Add(new Channel(channelField));
        }
        Channels = channels;
    }

    private List<int> GetStreamLengths(UnityVersion version)
    {
        var streamLengths = new List<int>();
        var streamCount = Channels.Max(c => c.stream) + 1;
        for (var i = 0; i < streamCount; i++)
        {
            var maxEndOffset = 0;
            for (var j = 0; j < Channels.Count; j++)
            {
                if (Channels[j].stream == i)
                {
                    var channel = Channels[j];
                    var size = GetFormatSize(ToVertexFormatV2(channel.format, version));
                    var endOffset = channel.offset + (channel.dimension & 0xf) * size;
                    maxEndOffset = endOffset > maxEndOffset ? endOffset : maxEndOffset;
                }
            }
            streamLengths.Add(maxEndOffset);
        }

        return streamLengths;
    }

    private static int GetFormatSize(VertexFormatV2 format)
    {
        return format switch
        {
            VertexFormatV2.Float => 4,
            VertexFormatV2.Float16 => 2,
            VertexFormatV2.UNorm8 => 1,
            VertexFormatV2.SNorm8 => 1,
            VertexFormatV2.UNorm16 => 2,
            VertexFormatV2.SNorm16 => 2,
            VertexFormatV2.UInt8 => 1,
            VertexFormatV2.SInt8 => 1,
            VertexFormatV2.UInt16 => 2,
            VertexFormatV2.SInt16 => 2,
            VertexFormatV2.UInt32 => 4,
            VertexFormatV2.SInt32 => 4,
            _ => throw new Exception($"Unknown format {format}")
        };
    }

    public void WriteVertexPositions(
        AssetsFileInstance fileInst,
        AssetTypeValueField baseField,
        UnityVersion version,
        IReadOnlyList<float> positions)
    {
        if (VertexCount <= 0 || positions.Count != VertexCount * 3)
        {
            throw new InvalidDataException($"Expected {VertexCount} OBJ vertices, got {positions.Count / 3}.");
        }

        var vertexChannel = GetVertexChannelOrThrow();
        var vertexFormat = ToVertexFormatV2(vertexChannel.format, version);
        if (vertexFormat != VertexFormatV2.Float && vertexFormat != VertexFormatV2.Float16)
        {
            throw new InvalidDataException($"Mesh vertex channel format {vertexFormat} cannot be edited safely.");
        }

        var dimension = vertexChannel.dimension & 0xf;
        if (dimension < 3)
        {
            throw new InvalidDataException("Mesh vertex channel has fewer than three dimensions.");
        }

        var vertexData = GetVertexData(fileInst, baseField);
        var streamLengths = GetStreamLengths(version);
        if (vertexChannel.stream >= streamLengths.Count)
        {
            throw new InvalidDataException("Mesh vertex channel references an invalid stream.");
        }

        var startPos = 0;
        for (var i = 0; i < vertexChannel.stream; i++)
        {
            startPos += streamLengths[i] * VertexCount;
        }

        var streamLength = streamLengths[vertexChannel.stream];
        var formatSize = GetFormatSize(vertexFormat);
        for (var i = 0; i < VertexCount; i++)
        {
            var dst = startPos + vertexChannel.offset + i * streamLength;
            for (var d = 0; d < 3; d++)
            {
                WriteVertexFloat(vertexData, dst + d * formatSize, positions[i * 3 + d], vertexFormat);
            }
        }

        var dataSizeField = baseField["m_VertexData"]["m_DataSize"];
        if (dataSizeField.IsDummy)
        {
            throw new InvalidDataException("Mesh vertex data field m_DataSize couldn't be found.");
        }

        dataSizeField.AsByteArray = vertexData;
        Vertices = positions.ToArray();

        ClearStreamData(baseField);
    }

    public void WriteImportedMesh(
        AssetsFileInstance fileInst,
        AssetTypeValueField baseField,
        UnityVersion version,
        ImportedMeshGeometry geometry)
    {
        if (geometry.VertexCount <= 0 || geometry.Vertices.Length < geometry.VertexCount * 3)
        {
            throw new InvalidDataException("Imported mesh has no vertices.");
        }

        if (geometry.Indices.Length < 3 || geometry.Indices.Length % 3 != 0)
        {
            throw new InvalidDataException("Imported mesh must contain triangle indices.");
        }

        if (Channels.Count == 0)
        {
            throw new InvalidDataException("Mesh has no vertex channels.");
        }

        var vertexChannel = GetVertexChannelOrThrow();
        var vertexFormat = ToVertexFormatV2(vertexChannel.format, version);
        if (vertexFormat != VertexFormatV2.Float && vertexFormat != VertexFormatV2.Float16)
        {
            throw new InvalidDataException($"Mesh vertex channel format {vertexFormat} cannot be replaced safely.");
        }

        var vertexCount = geometry.VertexCount;
        var streamLengths = GetStreamLengths(version);
        var totalSize = streamLengths.Sum(len => len * vertexCount);
        var vertexData = new byte[totalSize];

        var normals = geometry.Normals.Length >= vertexCount * 3
            ? geometry.Normals
            : CalculateImportedNormals(geometry.Vertices, geometry.Indices);

        for (var chnIdx = 0; chnIdx < Channels.Count; chnIdx++)
        {
            var channel = Channels[chnIdx];
            var dimension = channel.dimension & 0xf;
            if (dimension == 0 || channel.stream >= streamLengths.Count)
            {
                continue;
            }

            var channelFormat = ToVertexFormatV2(channel.format, version);
            var startPos = 0;
            for (var i = 0; i < channel.stream; i++)
            {
                startPos += streamLengths[i] * vertexCount;
            }

            var streamLength = streamLengths[channel.stream];
            for (var v = 0; v < vertexCount; v++)
            {
                var dst = startPos + channel.offset + v * streamLength;
                WriteImportedChannel(vertexData, dst, chnIdx, v, geometry, normals, version, channelFormat, dimension);
            }
        }

        var vertexDataField = baseField["m_VertexData"];
        vertexDataField["m_VertexCount"].AsInt = vertexCount;
        vertexDataField["m_DataSize"].AsByteArray = vertexData;

        var maxIndex = geometry.Indices.Max();
        var use16Bit = maxIndex < 65536;
        var indexFormatField = baseField["m_IndexFormat"];
        if (!indexFormatField.IsDummy)
        {
            indexFormatField.AsInt = use16Bit ? 0 : 1;
        }

        var indexSize = use16Bit ? 2 : 4;
        var indexBytes = new byte[geometry.Indices.Length * indexSize];
        for (var i = 0; i < geometry.Indices.Length; i++)
        {
            var dst = i * indexSize;
            if (use16Bit)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(indexBytes.AsSpan(dst, 2), (ushort)geometry.Indices[i]);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(indexBytes.AsSpan(dst, 4), geometry.Indices[i]);
            }
        }

        baseField["m_IndexBuffer.Array"].AsByteArray = indexBytes;
        UpdateSubMeshes(baseField, geometry.Indices, vertexCount, geometry.Vertices);
        UpdateLocalAabb(baseField, geometry.Vertices);
        ClearStreamData(baseField);

        VertexCount = vertexCount;
        Vertices = geometry.Vertices;
        Normals = normals;
        Indices = geometry.Indices;
        if (geometry.Uvs.Length >= vertexCount * 2)
        {
            if (UVs.Length == 0)
            {
                UVs = new float[8][];
            }

            UVs[0] = geometry.Uvs;
        }
    }

    private static void ClearStreamData(AssetTypeValueField baseField)
    {
        var streamData = baseField["m_StreamData"];
        if (streamData.IsDummy)
        {
            return;
        }

        var offsetField = streamData["offset"];
        if (!offsetField.IsDummy)
        {
            offsetField.AsUInt = 0;
        }

        var sizeField = streamData["size"];
        if (!sizeField.IsDummy)
        {
            sizeField.AsUInt = 0;
        }

        var pathField = streamData["path"];
        if (!pathField.IsDummy)
        {
            pathField.AsString = string.Empty;
        }
    }

    private static void UpdateSubMeshes(AssetTypeValueField baseField, uint[] indices, int vertexCount, float[] vertices)
    {
        var subMeshes = baseField["m_SubMeshes.Array"];
        if (subMeshes.IsDummy || subMeshes.Children.Count == 0)
        {
            return;
        }

        var minIndex = indices.Min();
        var maxIndex = indices.Max();
        var subMesh = subMeshes.Children[0];

        if (!subMesh["firstByte"].IsDummy)
        {
            subMesh["firstByte"].AsUInt = 0;
        }

        if (!subMesh["indexCount"].IsDummy)
        {
            subMesh["indexCount"].AsUInt = (uint)indices.Length;
        }

        if (!subMesh["topology"].IsDummy)
        {
            subMesh["topology"].AsInt = (int)GfxPrimitiveType.Triangles;
        }

        if (!subMesh["baseVertex"].IsDummy)
        {
            subMesh["baseVertex"].AsUInt = 0;
        }

        if (!subMesh["firstVertex"].IsDummy)
        {
            subMesh["firstVertex"].AsUInt = minIndex;
        }

        if (!subMesh["vertexCount"].IsDummy)
        {
            subMesh["vertexCount"].AsUInt = (uint)vertexCount;
        }

        UpdateBoundsField(subMesh["localAABB"], vertices, vertexCount, indices);
    }

    private static void UpdateLocalAabb(AssetTypeValueField baseField, float[] vertices)
    {
        UpdateBoundsField(baseField["m_LocalAABB"], vertices, vertices.Length / 3, null);
    }

    private static void UpdateBoundsField(
        AssetTypeValueField boundsField,
        float[] vertices,
        int vertexCount,
        uint[]? indices)
    {
        if (boundsField.IsDummy || vertices.Length < vertexCount * 3)
        {
            return;
        }

        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var minZ = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;
        var maxZ = float.MinValue;

        IEnumerable<int> vertexIndices = indices is null
            ? Enumerable.Range(0, vertexCount)
            : indices.Select(i => (int)i).Distinct();

        var any = false;
        foreach (var index in vertexIndices)
        {
            if (index < 0 || index >= vertexCount)
            {
                continue;
            }

            var x = vertices[index * 3];
            var y = vertices[index * 3 + 1];
            var z = vertices[index * 3 + 2];
            minX = MathF.Min(minX, x);
            minY = MathF.Min(minY, y);
            minZ = MathF.Min(minZ, z);
            maxX = MathF.Max(maxX, x);
            maxY = MathF.Max(maxY, y);
            maxZ = MathF.Max(maxZ, z);
            any = true;
        }

        if (!any)
        {
            return;
        }

        WriteVector3(boundsField["m_Center"],
            (minX + maxX) * 0.5f,
            (minY + maxY) * 0.5f,
            (minZ + maxZ) * 0.5f);
        WriteVector3(boundsField["m_Extent"],
            (maxX - minX) * 0.5f,
            (maxY - minY) * 0.5f,
            (maxZ - minZ) * 0.5f);
    }

    private static void WriteVector3(AssetTypeValueField field, float x, float y, float z)
    {
        if (field.IsDummy)
        {
            return;
        }

        if (!field["x"].IsDummy)
        {
            field["x"].AsFloat = x;
        }

        if (!field["y"].IsDummy)
        {
            field["y"].AsFloat = y;
        }

        if (!field["z"].IsDummy)
        {
            field["z"].AsFloat = z;
        }
    }

    private void WriteImportedChannel(
        byte[] vertexData,
        int dst,
        int channelIndex,
        int vertexIndex,
        ImportedMeshGeometry geometry,
        float[] normals,
        UnityVersion version,
        VertexFormatV2 format,
        int dimension)
    {
        Span<float> values = stackalloc float[4];
        values.Clear();

        if (version.major >= 2018)
        {
            FillImportedChannelV3(values, (ChannelTypeV3)channelIndex, vertexIndex, geometry, normals, dimension);
        }
        else
        {
            FillImportedChannelV2(values, (ChannelTypeV2)channelIndex, vertexIndex, geometry, normals, dimension);
        }

        WriteVertexComponents(vertexData, dst, format, values, dimension);
    }

    private static void FillImportedChannelV3(
        Span<float> values,
        ChannelTypeV3 channelType,
        int vertexIndex,
        ImportedMeshGeometry geometry,
        float[] normals,
        int dimension)
    {
        switch (channelType)
        {
            case ChannelTypeV3.Vertex:
                values[0] = geometry.Vertices[vertexIndex * 3];
                values[1] = geometry.Vertices[vertexIndex * 3 + 1];
                values[2] = geometry.Vertices[vertexIndex * 3 + 2];
                break;
            case ChannelTypeV3.Normal:
                values[0] = normals[vertexIndex * 3];
                values[1] = normals[vertexIndex * 3 + 1];
                values[2] = normals[vertexIndex * 3 + 2];
                break;
            case ChannelTypeV3.Tangent:
                values[0] = 1f;
                values[1] = 0f;
                values[2] = 0f;
                if (dimension >= 4)
                {
                    values[3] = 1f;
                }
                break;
            case ChannelTypeV3.Color:
                values[0] = 1f;
                values[1] = 1f;
                values[2] = 1f;
                if (dimension >= 4)
                {
                    values[3] = 1f;
                }
                break;
            case ChannelTypeV3.TexCoord0:
                if (geometry.Uvs.Length >= (vertexIndex + 1) * 2)
                {
                    values[0] = geometry.Uvs[vertexIndex * 2];
                    values[1] = geometry.Uvs[vertexIndex * 2 + 1];
                }
                break;
            case ChannelTypeV3.TexCoord1:
            case ChannelTypeV3.TexCoord2:
            case ChannelTypeV3.TexCoord3:
            case ChannelTypeV3.TexCoord4:
            case ChannelTypeV3.TexCoord5:
            case ChannelTypeV3.TexCoord6:
            case ChannelTypeV3.TexCoord7:
                break;
            case ChannelTypeV3.BlendWeight:
                values[0] = 1f;
                break;
            case ChannelTypeV3.BlendIndices:
                values[0] = 0f;
                break;
        }
    }

    private static void FillImportedChannelV2(
        Span<float> values,
        ChannelTypeV2 channelType,
        int vertexIndex,
        ImportedMeshGeometry geometry,
        float[] normals,
        int dimension)
    {
        switch (channelType)
        {
            case ChannelTypeV2.Vertex:
                values[0] = geometry.Vertices[vertexIndex * 3];
                values[1] = geometry.Vertices[vertexIndex * 3 + 1];
                values[2] = geometry.Vertices[vertexIndex * 3 + 2];
                break;
            case ChannelTypeV2.Normal:
                values[0] = normals[vertexIndex * 3];
                values[1] = normals[vertexIndex * 3 + 1];
                values[2] = normals[vertexIndex * 3 + 2];
                break;
            case ChannelTypeV2.Tangent:
                values[0] = 1f;
                values[1] = 0f;
                values[2] = 0f;
                if (dimension >= 4)
                {
                    values[3] = 1f;
                }
                break;
            case ChannelTypeV2.Color:
                values[0] = 1f;
                values[1] = 1f;
                values[2] = 1f;
                if (dimension >= 4)
                {
                    values[3] = 1f;
                }
                break;
            case ChannelTypeV2.TexCoord0:
                if (geometry.Uvs.Length >= (vertexIndex + 1) * 2)
                {
                    values[0] = geometry.Uvs[vertexIndex * 2];
                    values[1] = geometry.Uvs[vertexIndex * 2 + 1];
                }
                break;
            case ChannelTypeV2.TexCoord1:
            case ChannelTypeV2.TexCoord2:
            case ChannelTypeV2.TexCoord3:
                break;
        }
    }

    private static void WriteVertexComponents(
        byte[] data,
        int offset,
        VertexFormatV2 format,
        ReadOnlySpan<float> values,
        int dimension)
    {
        switch (format)
        {
            case VertexFormatV2.Float:
                for (var i = 0; i < dimension; i++)
                {
                    WriteVertexFloat(data, offset + i * 4, values[i], format);
                }
                break;
            case VertexFormatV2.Float16:
                for (var i = 0; i < dimension; i++)
                {
                    WriteVertexFloat(data, offset + i * 2, values[i], format);
                }
                break;
            case VertexFormatV2.UNorm8:
                for (var i = 0; i < dimension; i++)
                {
                    data[offset + i] = (byte)Math.Clamp((int)MathF.Round(values[i] * 255f), 0, 255);
                }
                break;
            case VertexFormatV2.SNorm8:
                for (var i = 0; i < dimension; i++)
                {
                    data[offset + i] = (byte)Math.Clamp((int)MathF.Round(values[i] * 127f), -127, 127);
                }
                break;
            case VertexFormatV2.UNorm16:
                for (var i = 0; i < dimension; i++)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        data.AsSpan(offset + i * 2, 2),
                        (ushort)Math.Clamp((int)MathF.Round(values[i] * 65535f), 0, 65535));
                }
                break;
            case VertexFormatV2.SNorm16:
                for (var i = 0; i < dimension; i++)
                {
                    BinaryPrimitives.WriteInt16LittleEndian(
                        data.AsSpan(offset + i * 2, 2),
                        (short)Math.Clamp((int)MathF.Round(values[i] * 32767f), -32767, 32767));
                }
                break;
            case VertexFormatV2.UInt8:
            case VertexFormatV2.SInt8:
            case VertexFormatV2.UInt16:
            case VertexFormatV2.SInt16:
            case VertexFormatV2.UInt32:
            case VertexFormatV2.SInt32:
                for (var i = 0; i < dimension; i++)
                {
                    WriteVertexFloat(data, offset + i * GetFormatSize(format), values[i], VertexFormatV2.Float);
                }
                break;
            default:
                throw new InvalidDataException($"Unsupported writable vertex format {format}.");
        }
    }

    private static float[] CalculateImportedNormals(float[] vertices, uint[] indices)
    {
        var vertexCount = vertices.Length / 3;
        var accum = new float[vertexCount * 3];
        var counts = new int[vertexCount];

        for (var i = 0; i + 2 < indices.Length; i += 3)
        {
            var ia = (int)indices[i];
            var ib = (int)indices[i + 1];
            var ic = (int)indices[i + 2];
            if (ia < 0 || ib < 0 || ic < 0 || ia >= vertexCount || ib >= vertexCount || ic >= vertexCount)
            {
                continue;
            }

            var ax = vertices[ia * 3];
            var ay = vertices[ia * 3 + 1];
            var az = vertices[ia * 3 + 2];
            var bx = vertices[ib * 3];
            var by = vertices[ib * 3 + 1];
            var bz = vertices[ib * 3 + 2];
            var cx = vertices[ic * 3];
            var cy = vertices[ic * 3 + 1];
            var cz = vertices[ic * 3 + 2];

            var ux = bx - ax;
            var uy = by - ay;
            var uz = bz - az;
            var vx = cx - ax;
            var vy = cy - ay;
            var vz = cz - az;

            var nx = uy * vz - uz * vy;
            var ny = uz * vx - ux * vz;
            var nz = ux * vy - uy * vx;
            var lenSq = nx * nx + ny * ny + nz * nz;
            if (lenSq < 1e-12f)
            {
                continue;
            }

            var invLen = 1f / MathF.Sqrt(lenSq);
            nx *= invLen;
            ny *= invLen;
            nz *= invLen;

            accum[ia * 3] += nx;
            accum[ia * 3 + 1] += ny;
            accum[ia * 3 + 2] += nz;
            accum[ib * 3] += nx;
            accum[ib * 3 + 1] += ny;
            accum[ib * 3 + 2] += nz;
            accum[ic * 3] += nx;
            accum[ic * 3 + 1] += ny;
            accum[ic * 3 + 2] += nz;
            counts[ia]++;
            counts[ib]++;
            counts[ic]++;
        }

        var normals = new float[vertexCount * 3];
        for (var v = 0; v < vertexCount; v++)
        {
            if (counts[v] == 0)
            {
                normals[v * 3 + 1] = 1f;
                continue;
            }

            var nx = accum[v * 3] / counts[v];
            var ny = accum[v * 3 + 1] / counts[v];
            var nz = accum[v * 3 + 2] / counts[v];
            var lenSq = nx * nx + ny * ny + nz * nz;
            if (lenSq < 1e-12f)
            {
                normals[v * 3 + 1] = 1f;
                continue;
            }

            var invLen = 1f / MathF.Sqrt(lenSq);
            normals[v * 3] = nx * invLen;
            normals[v * 3 + 1] = ny * invLen;
            normals[v * 3 + 2] = nz * invLen;
        }

        return normals;
    }

    private static void WriteVertexFloat(byte[] data, int offset, float value, VertexFormatV2 format)
    {
        switch (format)
        {
            case VertexFormatV2.Float:
                BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, 4), BitConverter.SingleToInt32Bits(value));
                break;
            case VertexFormatV2.Float16:
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, 2), BitConverter.HalfToUInt16Bits((Half)value));
                break;
            default:
                throw new InvalidDataException($"Unsupported writable vertex format {format}.");
        }
    }

    private static VertexFormatV2 ToVertexFormatV2(int format, UnityVersion version)
    {
        if (version.major >= 2019)
        {
            return (VertexFormatV2)format;
        }
        else if (version.major >= 2017)
        {
            return (VertexFormatV1)format switch
            {
                VertexFormatV1.Float => VertexFormatV2.Float,
                VertexFormatV1.Float16 => VertexFormatV2.Float16,
                VertexFormatV1.Color or
                VertexFormatV1.UNorm8 => VertexFormatV2.UNorm8,
                VertexFormatV1.SNorm8 => VertexFormatV2.SNorm8,
                VertexFormatV1.UNorm16 => VertexFormatV2.UNorm16,
                VertexFormatV1.SNorm16 => VertexFormatV2.SNorm16,
                VertexFormatV1.UInt8 => VertexFormatV2.UInt8,
                VertexFormatV1.SInt8 => VertexFormatV2.SInt8,
                VertexFormatV1.UInt16 => VertexFormatV2.UInt16,
                VertexFormatV1.SInt16 => VertexFormatV2.SInt16,
                VertexFormatV1.UInt32 => VertexFormatV2.UInt32,
                VertexFormatV1.SInt32 => VertexFormatV2.SInt32,
                _ => throw new Exception($"Unknown format {format}")
            };
        }
        else
        {
            return (VertexChannelFormat)format switch
            {
                VertexChannelFormat.Float => VertexFormatV2.Float,
                VertexChannelFormat.Float16 => VertexFormatV2.Float16,
                VertexChannelFormat.Color => VertexFormatV2.UNorm8,
                VertexChannelFormat.Byte => VertexFormatV2.UInt8,
                VertexChannelFormat.UInt32 => VertexFormatV2.UInt32,
                _ => throw new Exception($"Unknown format {format}")
            };
        }
    }

    private Channel GetVertexChannelOrThrow()
    {
        if (Channels.Count == 0)
        {
            throw new InvalidDataException("Mesh has no vertex channels.");
        }

        // Unity m_Channels index 0 is always the vertex channel.
        var vertexChannel = Channels[0];
        if ((vertexChannel.dimension & 0xf) < 3)
        {
            throw new InvalidDataException("Mesh vertex channel is disabled or has fewer than three dimensions.");
        }

        return vertexChannel;
    }

    private static bool IsFormatInt(VertexFormatV2 format)
    {
        return format switch
        {
            VertexFormatV2.UInt8 => true,
            VertexFormatV2.SInt8 => true,
            VertexFormatV2.UInt16 => true,
            VertexFormatV2.SInt16 => true,
            VertexFormatV2.UInt32 => true,
            VertexFormatV2.SInt32 => true,
            _ => false
        };
    }

    private static byte[] GetVertexData(AssetsFileInstance fileInst, AssetTypeValueField baseField)
    {
        var usesStreamData = false;
        var offset = 0U;
        var size = 0U;
        var path = string.Empty;

        var streamData = baseField["m_StreamData"];
        if (!streamData.IsDummy)
        {
            offset = streamData["offset"].AsUInt;
            size = streamData["size"].AsUInt;
            path = streamData["path"].AsString;
            usesStreamData = size > 0 && path != string.Empty;
        }

        if (usesStreamData)
        {
            if (fileInst.parentBundle != null && path.StartsWith("archive:/"))
            {
                var archiveTrimmedPath = path;
                if (archiveTrimmedPath.StartsWith("archive:/"))
                    archiveTrimmedPath = archiveTrimmedPath.Substring(9);

                archiveTrimmedPath = Path.GetFileName(archiveTrimmedPath);

                AssetBundleFile bundle = fileInst.parentBundle.file;

                AssetsFileReader reader = bundle.DataReader;
                List<AssetBundleDirectoryInfo> dirInf = bundle.BlockAndDirInfo.DirectoryInfos;
                for (int i = 0; i < dirInf.Count; i++)
                {
                    AssetBundleDirectoryInfo info = dirInf[i];
                    if (info.Name == archiveTrimmedPath)
                    {
                        byte[] meshData;
                        lock (bundle.DataReader)
                        {
                            reader.Position = info.Offset + offset;
                            meshData = reader.ReadBytes((int)size);
                        }
                        return meshData;
                    }
                }
            }

            var rootPath = Path.GetDirectoryName(fileInst.path)
                ?? throw new FileNotFoundException("Can't find resS for mesh");

            var fixedStreamPath = path;

            // user may have extracted serialized file and resS from bundle to disk
            var bundleInst = fileInst.parentBundle;
            if (bundleInst == null && path.StartsWith("archive:/"))
            {
                fixedStreamPath = Path.GetFileName(fixedStreamPath);
            }
            if (!Path.IsPathRooted(fixedStreamPath) && rootPath != null)
            {
                fixedStreamPath = Path.Combine(rootPath, fixedStreamPath);
            }

            if (File.Exists(fixedStreamPath))
            {
                var stream = File.OpenRead(fixedStreamPath);
                stream.Position = offset;
                var data = new byte[size];
                stream.Read(data, 0, (int)size);
                return data;
            }
            // we still haven't found it yet. maybe a data.unity3d bundle?
            // in this case, we won't have the archive:/ prefix, so use the original path
            else if (bundleInst != null && TryGetBundleFileIndex(bundleInst.file, path, out var fileIdx))
            {
                var bundle = bundleInst.file;
                bundle.GetFileRange(fileIdx, out var bunOffset, out var _);
                var reader = bundle.DataReader;
                reader.Position = bunOffset + offset;
                return reader.ReadBytes((int)size);
            }
            else
            {
                throw new FileNotFoundException("Can't find resS for mesh");
            }
        }
        else
        {
            return baseField["m_VertexData"]["m_DataSize"].AsByteArray;
        }
    }

    private static bool TryGetBundleFileIndex(AssetBundleFile bunFile, string name, out int dirInf)
    {
        dirInf = bunFile.BlockAndDirInfo.DirectoryInfos.FindIndex(i => i.Name == name);
        return dirInf != -1;
    }

    private void ReadVertexData(AssetsFileInstance fileInst, AssetTypeValueField baseField, UnityVersion version)
    {
        var vertexCount = baseField["m_VertexData"]["m_VertexCount"].AsInt;
        VertexCount = vertexCount;
        var vertexData = GetVertexData(fileInst, baseField);
        var streamLengths = GetStreamLengths(version);
        var startPos = 0;
        for (var strIdx = 0; strIdx < streamLengths.Count; strIdx++)
        {
            var streamLength = streamLengths[strIdx];
            for (var chnIdx = 0; chnIdx < Channels.Count; chnIdx++)
            {
                var channel = Channels[chnIdx];
                if (channel.stream != strIdx)
                    continue;

                var dimension = channel.dimension & 0xf;
                var vertexFormat = ToVertexFormatV2(channel.format, version);
                var offset = channel.offset + startPos;
                var size = GetFormatSize(vertexFormat) * dimension;
                var data = new byte[size * vertexCount];
                for (var i = 0; i < vertexCount; i++)
                {
                    Buffer.BlockCopy(vertexData, offset + i * streamLength, data, i * size, size);
                }

                int[]? intItems = null;
                float[]? floatItems = null;
                if (IsFormatInt(vertexFormat))
                    intItems = ConvertIntArray(data, dimension, vertexFormat);
                else
                    floatItems = ConvertFloatArray(data, dimension, vertexFormat);

                SetCorrectArray(intItems!, floatItems!, chnIdx, version);
            }
            startPos += streamLengths[strIdx] * vertexCount;
        }
    }

    private void SetCorrectArray(int[] intItems, float[] floatItems, int channelIndex, UnityVersion version)
    {
        if (version.major >= 2018)
        {
            var channelType = (ChannelTypeV3)channelIndex;
            switch (channelType)
            {
                case ChannelTypeV3.Vertex: Vertices = floatItems; break;
                case ChannelTypeV3.Normal: Normals = floatItems; break;
                case ChannelTypeV3.Tangent: Tangents = floatItems; break;
                case ChannelTypeV3.Color: Colors = floatItems; break;
                case ChannelTypeV3.TexCoord0:
                case ChannelTypeV3.TexCoord1:
                case ChannelTypeV3.TexCoord2:
                case ChannelTypeV3.TexCoord3:
                case ChannelTypeV3.TexCoord4:
                case ChannelTypeV3.TexCoord5:
                case ChannelTypeV3.TexCoord6:
                case ChannelTypeV3.TexCoord7:
                {
                    if (UVs.Length == 0)
                    {
                        UVs = new float[8][];
                    }
                    UVs[(int)channelType - (int)ChannelTypeV3.TexCoord0] = floatItems;
                    break;
                }
                case ChannelTypeV3.BlendWeight:
                case ChannelTypeV3.BlendIndices:
                {
                    // ignore for now
                    break;
                }
            }
        }
        else // if (version.major >= 5)
        {
            var channelType = (ChannelTypeV2)channelIndex;
            switch (channelType)
            {
                case ChannelTypeV2.Vertex: Vertices = floatItems; break;
                case ChannelTypeV2.Normal: Normals = floatItems; break;
                case ChannelTypeV2.Color: Colors = floatItems; break;
                case ChannelTypeV2.TexCoord0:
                case ChannelTypeV2.TexCoord1:
                case ChannelTypeV2.TexCoord2:
                case ChannelTypeV2.TexCoord3:
                {
                    if (UVs.Length == 0)
                    {
                        UVs = new float[4][];
                    }
                    UVs[(int)channelType - (int)ChannelTypeV2.TexCoord0] = floatItems;
                    break;
                }
                case ChannelTypeV2.Tangent: Tangents = floatItems; break;
            }
        }
    }

    private static int[] ConvertIntArray(byte[] data, int dims, VertexFormatV2 format)
    {
        var size = GetFormatSize(format);
        var count = data.Length / size;
        var items = new int[count];
        switch (format)
        {
            case VertexFormatV2.UInt8:
            case VertexFormatV2.SInt8:
            {
                for (var i = 0; i < count; i++)
                {
                    items[i] = data[i];
                }
                return items;
            }
            case VertexFormatV2.UInt16:
            case VertexFormatV2.SInt16:
            {
                var src = 0;
                for (var i = 0; i < count; i++, src += 2)
                {
                    items[i] = data[src] | data[src + 1] << 8;
                }
                return items;
            }
            case VertexFormatV2.UInt32:
            case VertexFormatV2.SInt32:
            {
                var src = 0;
                for (var i = 0; i < count; i++, src += 4)
                {
                    items[i] = data[src] | data[src + 1] << 8 | data[src + 2] << 16 | data[src + 3] << 24;
                }
                return items;
            }
            default:
                throw new Exception($"Unknown format {format}");
        }
    }

    private static float[] ConvertFloatArray(byte[] data, int dims, VertexFormatV2 format)
    {
        var size = GetFormatSize(format);
        var count = data.Length / size;
        var items = new float[count];
        switch (format)
        {
            case VertexFormatV2.Float:
            {
                var src = 0;
                for (var i = 0; i < count; i++, src += 4)
                {
                    items[i] = BitConverter.ToSingle(data, src);
                }
                return items;
            }
            case VertexFormatV2.Float16:
            {
                var src = 0;
                for (var i = 0; i < count; i++, src += 2)
                {
                    items[i] = (float)BitConverter.UInt16BitsToHalf((ushort)(data[src] | data[src + 1] << 8));
                }
                return items;
            }
            case VertexFormatV2.UNorm8:
            {
                for (var i = 0; i < count; i++)
                {
                    items[i] = data[i] / 255f;
                }
                return items;
            }
            case VertexFormatV2.SNorm8:
            {
                for (var i = 0; i < count; i++)
                {
                    items[i] = Math.Max((sbyte)data[i] / 127f, -1f);
                }
                return items;
            }
            case VertexFormatV2.UNorm16:
            {
                var src = 0;
                for (var i = 0; i < count; i++, src += 2)
                {
                    items[i] = (data[src] | data[src + 1] << 8) / 65535f;
                }
                return items;
            }
            case VertexFormatV2.SNorm16:
            {
                var src = 0;
                for (var i = 0; i < count; i++, src += 2)
                {
                    items[i] = Math.Max((short)(data[src] | data[src + 1] << 8) / 32767f, -1f);
                }
                return items;
            }
            default:
                throw new Exception($"Unknown format {format}");
        }
    }
}
