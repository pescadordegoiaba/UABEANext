using System;
using System.Collections.Generic;
using System.Text;

namespace Il2CppDumper.Protection;

/// <summary>
/// Prepares protected global-metadata and IL2CPP binaries (e.g. mprotector-style XOR/obfuscation)
/// before Il2CppDumper parses them.
/// </summary>
public static class Il2CppProtectionBypass
{
    public static readonly byte[] MetadataMagic = [0xAF, 0x1B, 0xB1, 0xFA];

    public static byte[] PrepareMetadata(byte[] data, out string? methodUsed)
    {
        return PrepareMetadata(data, metadataPath: null, out methodUsed);
    }

    public static byte[] PrepareMetadata(byte[] data, string? metadataPath, out string? methodUsed)
    {
        methodUsed = "none";
        if (IsValidMetadata(data))
        {
            return data;
        }

        if (BlockStrikeMetadataBypass.TryPrepare(data, metadataPath, out var blockStrike, out var bsMethod))
        {
            methodUsed = bsMethod;
            return blockStrike;
        }

        if (TryXorSingleByteRestoreMagic(data, out var singleXor, out var key))
        {
            methodUsed = $"xor-single-byte-0x{key:X2}";
            return singleXor;
        }

        if (TryOffsetRestoreMagic(data, out var offsetFixed))
        {
            methodUsed = "header-offset-fix";
            return offsetFixed;
        }

        foreach (var pattern in CommonXorKeys)
        {
            if (TryXorRepeating(data, pattern, out var decoded) && IsValidMetadata(decoded))
            {
                methodUsed = $"xor-repeating-{Encoding.ASCII.GetString(pattern)}";
                return decoded;
            }
        }

        if (TryByteSwap32(data, out var swapped) && IsValidMetadata(swapped))
        {
            methodUsed = "swap32";
            return swapped;
        }

        var embedded = FindEmbeddedMetadata(data);
        if (embedded != null)
        {
            methodUsed = "embedded-blob";
            return embedded;
        }

        methodUsed = null;
        return data;
    }

    public static byte[]? TryExtractMetadataFromBinary(byte[] binary, int maxHits = 8)
    {
        var hits = new List<byte[]>();
        for (var i = 0; i <= binary.Length - MetadataMagic.Length; i++)
        {
            if (binary[i] != MetadataMagic[0] || binary[i + 1] != MetadataMagic[1] ||
                binary[i + 2] != MetadataMagic[2] || binary[i + 3] != MetadataMagic[3])
            {
                continue;
            }

            var sliceLen = Math.Min(binary.Length - i, 64 * 1024 * 1024);
            var slice = new byte[sliceLen];
            Buffer.BlockCopy(binary, i, slice, 0, sliceLen);
            if (IsValidMetadata(slice))
            {
                hits.Add(slice);
                if (hits.Count >= maxHits)
                {
                    break;
                }
            }
        }

        if (hits.Count == 0)
        {
            for (var key = 0; key < 256; key++)
            {
                if (!TryXorSingleByteRestoreMagic(binary, out var xored, out _) || xored == binary)
                {
                    continue;
                }

                var found = FindEmbeddedMetadata(xored);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        byte[]? best = null;
        var bestScore = -1;
        foreach (var candidate in hits)
        {
            var score = ScoreMetadata(candidate);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    public static byte[] PrepareIl2CppBinary(byte[] data, out string? methodUsed)
    {
        methodUsed = "none";
        if (data.Length < 4)
        {
            return data;
        }

        var magic = BitConverter.ToUInt32(data, 0);
        if (magic is 0x905A4D or 0x464c457f or 0xCAFEBABE or 0xBEBAFECA or 0xFEEDFACE or 0xFEEDFACF or 0x304F534E or 0x6D736100)
        {
            return data;
        }

        if (TryXorSingleByteRestorePeElfMagic(data, out var restored, out var key))
        {
            methodUsed = $"binary-xor-single-0x{key:X2}";
            return restored;
        }

        methodUsed = null;
        return data;
    }

    public static bool IsValidMetadata(byte[] data)
    {
        if (data.Length < 0x100)
        {
            return false;
        }

        if (data[0] != MetadataMagic[0] || data[1] != MetadataMagic[1] || data[2] != MetadataMagic[2] || data[3] != MetadataMagic[3])
        {
            return false;
        }

        var version = BitConverter.ToUInt32(data, 4);
        if (version is < 16 or > 40)
        {
            return false;
        }

        if (data.Length < 0x18)
        {
            return false;
        }

        var stringLiteralCount = BitConverter.ToInt32(data, 0x0C);
        var stringLiteralOffset = BitConverter.ToUInt32(data, 0x10);
        if (stringLiteralCount < 0 || stringLiteralCount > 5_000_000)
        {
            return false;
        }

        if (stringLiteralOffset >= (ulong)data.Length)
        {
            return false;
        }

        return ScoreMetadata(data) >= 4;
    }

    private static int ScoreMetadata(byte[] data)
    {
        var score = 0;
        if (IsValidMetadataHeaderOnly(data))
        {
            score += 4;
        }

        try
        {
            var version = BitConverter.ToUInt32(data, 4);
            if (version >= 24)
            {
                score += 1;
            }

            var imagesOffset = BitConverter.ToUInt32(data, 0x18 + (version >= 24 ? 4 : 0));
            if (imagesOffset > 0 && imagesOffset < data.Length)
            {
                score += 1;
            }
        }
        catch
        {
            // ignore
        }

        return score;
    }

    private static bool IsValidMetadataHeaderOnly(byte[] data)
    {
        if (data.Length < 8)
        {
            return false;
        }

        var version = BitConverter.ToUInt32(data, 4);
        return version is >= 16 and <= 40;
    }

    private static byte[]? FindEmbeddedMetadata(byte[] data)
    {
        for (var i = 0; i <= data.Length - 0x100; i++)
        {
            if (data[i] != MetadataMagic[0])
            {
                continue;
            }

            if (i + 4 > data.Length)
            {
                break;
            }

            if (data[i + 1] != MetadataMagic[1] || data[i + 2] != MetadataMagic[2] || data[i + 3] != MetadataMagic[3])
            {
                continue;
            }

            var len = Math.Min(data.Length - i, 32 * 1024 * 1024);
            var slice = new byte[len];
            Buffer.BlockCopy(data, i, slice, 0, len);
            if (IsValidMetadata(slice))
            {
                return slice;
            }
        }

        return null;
    }

    private static bool TryXorSingleByteRestoreMagic(byte[] data, out byte[] result, out byte key)
    {
        result = data;
        key = 0;
        if (data.Length < 4)
        {
            return false;
        }

        for (var k = 0; k < 256; k++)
        {
            var b0 = (byte)(data[0] ^ k);
            var b1 = (byte)(data[1] ^ k);
            var b2 = (byte)(data[2] ^ k);
            var b3 = (byte)(data[3] ^ k);
            if (b0 == MetadataMagic[0] && b1 == MetadataMagic[1] && b2 == MetadataMagic[2] && b3 == MetadataMagic[3])
            {
                var decoded = new byte[data.Length];
                for (var i = 0; i < data.Length; i++)
                {
                    decoded[i] = (byte)(data[i] ^ k);
                }

                if (IsValidMetadata(decoded))
                {
                    result = decoded;
                    key = (byte)k;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryXorSingleByteRestorePeElfMagic(byte[] data, out byte[] result, out byte key)
    {
        result = data;
        key = 0;
        if (data.Length < 2)
        {
            return false;
        }

        // PE 'MZ'
        Span<byte> peMagic = [0x4D, 0x5A];
        Span<byte> elfMagic = [0x7F, 0x45, 0x4C, 0x46];

        for (var k = 0; k < 256; k++)
        {
            if ((byte)(data[0] ^ k) == peMagic[0] && (byte)(data[1] ^ k) == peMagic[1])
            {
                var decoded = XorSingle(data, (byte)k);
                result = decoded;
                key = (byte)k;
                return true;
            }

            if (data.Length >= 4 &&
                (byte)(data[0] ^ k) == elfMagic[0] && (byte)(data[1] ^ k) == elfMagic[1] &&
                (byte)(data[2] ^ k) == elfMagic[2] && (byte)(data[3] ^ k) == elfMagic[3])
            {
                var decoded = XorSingle(data, (byte)k);
                result = decoded;
                key = (byte)k;
                return true;
            }
        }

        return false;
    }

    private static byte[] XorSingle(byte[] data, byte key)
    {
        var decoded = new byte[data.Length];
        for (var i = 0; i < data.Length; i++)
        {
            decoded[i] = (byte)(data[i] ^ key);
        }

        return decoded;
    }

    private static bool TryOffsetRestoreMagic(byte[] data, out byte[] result)
    {
        result = data;
        foreach (var skip in new[] { 4, 8, 16, 32, 64, 128, 256, 512, 1024 })
        {
            if (data.Length <= skip + 0x100)
            {
                continue;
            }

            var slice = new byte[data.Length - skip];
            Buffer.BlockCopy(data, skip, slice, 0, slice.Length);
            if (IsValidMetadata(slice))
            {
                result = slice;
                return true;
            }
        }

        return false;
    }

    private static bool TryXorRepeating(byte[] data, byte[] key, out byte[] result)
    {
        result = new byte[data.Length];
        for (var i = 0; i < data.Length; i++)
        {
            result[i] = (byte)(data[i] ^ key[i % key.Length]);
        }

        return true;
    }

    private static bool TryByteSwap32(byte[] data, out byte[] result)
    {
        if (data.Length < 4)
        {
            result = data;
            return false;
        }

        result = (byte[])data.Clone();
        for (var i = 0; i + 3 < result.Length; i += 4)
        {
            (result[i], result[i + 3]) = (result[i + 3], result[i]);
            (result[i + 1], result[i + 2]) = (result[i + 2], result[i + 1]);
        }

        return true;
    }

    private static readonly byte[][] CommonXorKeys =
    [
        "mprotector"u8.ToArray(),
        "MProtector"u8.ToArray(),
        "global-metadata"u8.ToArray(),
        "il2cpp"u8.ToArray(),
        [0x13, 0x37, 0x13, 0x37],
        [0x5A, 0x5A, 0x5A, 0x5A],
    ];
}