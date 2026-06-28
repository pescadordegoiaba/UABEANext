using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Il2CppDumper.Protection;

/// <summary>
/// Block Strike (com.rexetstudio.blockstrike) encrypts global-metadata.dat at rest
/// (TEA-like + XOR; filename built with keys -93..-75). Static decrypt is not public — try
/// sidecar dumps and lightweight heuristics before falling back to Frida memory dump.
/// </summary>
public static class BlockStrikeMetadataBypass
{
    public const int ExpectedFileSize = 8040192;
    public static readonly byte[] EncryptedHeaderSignature = [0xF9, 0x5D, 0x82, 0xFF];

    public static bool IsBlockStrikeEncrypted(byte[] data) =>
        data.Length == ExpectedFileSize &&
        data.Length >= 4 &&
        data[0] == EncryptedHeaderSignature[0] &&
        data[1] == EncryptedHeaderSignature[1] &&
        data[2] == EncryptedHeaderSignature[2] &&
        data[3] == EncryptedHeaderSignature[3];

    public static bool TryPrepare(byte[] data, string? metadataPath, out byte[] result, out string? methodUsed)
    {
        result = data;
        methodUsed = null;

        if (!IsBlockStrikeEncrypted(data))
        {
            return false;
        }

        if (TryLoadSidecar(metadataPath, out var sidecar, out var sidecarMethod))
        {
            result = sidecar;
            methodUsed = sidecarMethod;
            return true;
        }

        if (TryDecryptTeaXor(data, out var decrypted, out var teaMethod))
        {
            result = decrypted;
            methodUsed = teaMethod;
            return Il2CppProtectionBypass.IsValidMetadata(result);
        }

        methodUsed = null;
        return false;
    }

    public static string BuildFridaHint(string? metadataPath) =>
        "Block Strike: global-metadata.dat está criptografado no APK (TEA/XOR customizado).\n" +
        "Bypass estático não disponível para esta versão — use dump da memória:\n" +
        "  1) Instale o APK em um Android root/emulador com Frida.\n" +
        "  2) Execute: eng/il2cpp/frida-dump-blockstrike-metadata.sh\n" +
        "  3) Coloque o arquivo gerado como global-metadata-decrypted.dat ao lado do metadata original"
        + (string.IsNullOrEmpty(metadataPath)
            ? "."
            : $" ({Path.GetDirectoryName(metadataPath)}).") + "\n" +
        "  4) Rode o Il2CppDumper novamente com libil2cpp.so + o metadata descriptografado.\n" +
        "Pacote: com.rexetstudio.blockstrike | Tamanho esperado: 8040192 bytes | Unity 2021.3.45f1";

    private static bool TryLoadSidecar(string? metadataPath, out byte[] data, out string method)
    {
        data = [];
        method = "";
        if (string.IsNullOrEmpty(metadataPath))
        {
            return false;
        }

        var dir = Path.GetDirectoryName(metadataPath) ?? ".";
        foreach (var name in new[]
                 {
                     "global-metadata-decrypted.dat",
                     "global-metadata.dump.dat",
                     "dec_global-metadata.dat",
                     "global-metadata-uabea.dat",
                 })
        {
            var path = Path.Combine(dir, name);
            if (!File.Exists(path))
            {
                continue;
            }

            var bytes = File.ReadAllBytes(path);
            if (Il2CppProtectionBypass.IsValidMetadata(bytes))
            {
                data = bytes;
                method = $"sidecar:{name}";
                return true;
            }
        }

        return false;
    }

    private static bool TryDecryptTeaXor(byte[] data, out byte[] result, out string method)
    {
        result = data;
        method = "";

        var keys = BuildCandidateKeys();
        foreach (var key in keys)
        {
            if (TryDecryptTeaXorWithKey(data, key, out var decoded) &&
                Il2CppProtectionBypass.IsValidMetadata(decoded))
            {
                result = decoded;
                method = $"blockstrike-tea-xor-{key.Label}";
                return true;
            }
        }

        return false;
    }

    private static (byte[] Key, string Label)[] BuildCandidateKeys()
    {
        var list = new System.Collections.Generic.List<(byte[], string)>();
        var pkg = Encoding.UTF8.GetBytes("com.rexetstudio.blockstrike");
        list.Add((MD5.HashData(pkg), "md5-package"));
        list.Add((MD5.HashData("global-metadata.dat"u8.ToArray()), "md5-filename"));
        list.Add((BuildFilenameKeySchedule(), "filename-keys-93-75"));

        var sched = BuildFilenameKeySchedule();
        var mixed = new byte[16];
        for (var i = 0; i < 16; i++)
        {
            mixed[i] = (byte)(pkg[i % pkg.Length] ^ sched[i % sched.Length]);
        }

        list.Add((mixed, "package-xor-filename-schedule"));
        return list.ToArray();
    }

    private static byte[] BuildFilenameKeySchedule()
    {
        var key = new byte[19];
        for (var i = 0; i < 19; i++)
        {
            key[i] = (byte)(-93 + i);
        }

        return key;
    }

    private static bool TryDecryptTeaXorWithKey(byte[] data, (byte[] Key, string Label) keyInfo, out byte[] result)
    {
        var key = keyInfo.Key;
        if (key.Length < 16)
        {
            var padded = new byte[16];
            Buffer.BlockCopy(key, 0, padded, 0, key.Length);
            key = padded;
        }

        var tea = DecryptTeaBlocks(data, key);
        var xor = new byte[tea.Length];
        for (var i = 0; i < tea.Length; i++)
        {
            xor[i] = (byte)(tea[i] ^ key[i % key.Length]);
        }

        result = xor;
        return true;
    }

    private static byte[] DecryptTeaBlocks(byte[] data, byte[] key16)
    {
        var k0 = BitConverter.ToUInt32(key16, 0);
        var k1 = BitConverter.ToUInt32(key16, 4);
        var k2 = BitConverter.ToUInt32(key16, 8);
        var k3 = BitConverter.ToUInt32(key16, 12);
        const uint delta = 0x9E3779B9;

        var outLen = data.Length - (data.Length % 8);
        var result = new byte[outLen];
        Buffer.BlockCopy(data, 0, result, 0, outLen);

        for (var offset = 0; offset < outLen; offset += 8)
        {
            var v0 = BitConverter.ToUInt32(result, offset);
            var v1 = BitConverter.ToUInt32(result, offset + 4);
            var sum = unchecked(delta * 32);
            for (var round = 0; round < 32; round++)
            {
                v1 = unchecked(v1 - (((v0 << 4) + k2) ^ (v0 + sum) ^ ((v0 >> 5) + k3)));
                sum = unchecked(sum - delta);
                v0 = unchecked(v0 - (((v1 << 4) + k0) ^ (v1 + sum) ^ ((v1 >> 5) + k1)));
            }

            BitConverter.GetBytes(v0).CopyTo(result, offset);
            BitConverter.GetBytes(v1).CopyTo(result, offset + 4);
        }

        return result;
    }
}