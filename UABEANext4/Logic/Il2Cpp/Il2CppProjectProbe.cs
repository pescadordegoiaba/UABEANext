using Il2CppDumper.Protection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UABEANext4.Logic.Il2Cpp;

public enum UnityScriptBackend
{
    Unknown,
    Mono,
    Il2Cpp
}

public sealed class Il2CppProjectProbeResult
{
    public UnityScriptBackend Backend { get; init; }
    public string? Il2CppBinaryPath { get; init; }
    public string? MetadataPath { get; init; }
    public string? ManagedDirectory { get; init; }
    public string? AssetsDataDirectory { get; init; }
    public string Summary { get; init; } = "";
    public bool CanRunIl2CppDumper => Backend == UnityScriptBackend.Il2Cpp &&
                                      !string.IsNullOrEmpty(Il2CppBinaryPath);
}

public static class Il2CppProjectProbe
{
    private const int MaxRecursiveSearchDepth = 8;

    private static readonly string[] MetadataFileNames =
    [
        "global-metadata-uabea.dat",
        "global-metadata-decrypted.dat",
        "global-metadata.dump.dat",
        "dec_global-metadata.dat",
        "global-metadata.dat",
    ];

    private static readonly string[] Il2CppBinaryNames =
    [
        "libil2cpp.so",
        "libil2cpp.dbg.so",
        "GameAssembly.dll",
        "GameAssembly.so",
    ];

    /// <summary>
    /// Fast probe used after opening assets. Only checks known Unity layout paths near the file.
    /// </summary>
    public static Il2CppProjectProbeResult ProbeNearOpenedAsset(string fileOrDirPath)
    {
        if (string.IsNullOrWhiteSpace(fileOrDirPath))
        {
            return Unknown("Caminho vazio.");
        }

        var startDir = File.Exists(fileOrDirPath)
            ? Path.GetDirectoryName(fileOrDirPath)
            : fileOrDirPath;

        if (string.IsNullOrEmpty(startDir) || !Directory.Exists(startDir))
        {
            return Unknown("Pasta do arquivo não encontrada.");
        }

        for (var dir = startDir; dir != null; dir = Path.GetDirectoryName(dir))
        {
            var layout = ProbeKnownLayout(dir);
            if (layout.Backend != UnityScriptBackend.Unknown)
            {
                return layout;
            }
        }

        return Unknown("Nenhum layout Unity Mono/IL2CPP encontrado perto do arquivo aberto.");
    }

    public static Il2CppProjectProbeResult Probe(string root)
    {
        root = root.Trim();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return Unknown("Pasta inválida ou inexistente.");
        }

        root = Path.GetFullPath(root);
        if (File.Exists(root))
        {
            return ProbeNearBinary(root);
        }

        var quick = ProbeKnownLayout(root);
        if (quick.Backend != UnityScriptBackend.Unknown)
        {
            return quick;
        }

        if (IsBroadFilesystemRoot(root) || !LooksLikeUnityGameRoot(root))
        {
            return Unknown("Não foi possível detectar Mono nem IL2CPP nesta pasta.");
        }

        var managedDirs = FindManagedDirectories(root, MaxRecursiveSearchDepth).ToList();
        var metadataFiles = FindMetadataFiles(root, MaxRecursiveSearchDepth).ToList();
        var il2cppBins = FindIl2CppBinaries(root, MaxRecursiveSearchDepth).ToList();
        var hasLibMono = SafeEnumerateFiles(root, "libmono.so", MaxRecursiveSearchDepth).Any();
        var hasLibUnity = SafeEnumerateFiles(root, "libunity.so", MaxRecursiveSearchDepth).Any();
        var assetsDataDir = FindAssetsDataDirectory(root);

        return BuildProbeResult(managedDirs, metadataFiles, il2cppBins, hasLibMono, hasLibUnity, assetsDataDir);
    }

    public static Il2CppProjectProbeResult ProbeNearBinary(string binaryPath)
    {
        if (!File.Exists(binaryPath))
        {
            return Unknown($"Arquivo não encontrado: {binaryPath}");
        }

        var fileName = Path.GetFileName(binaryPath);
        if (Il2CppBinaryNames.Any(n => string.Equals(n, fileName, StringComparison.OrdinalIgnoreCase)))
        {
            var dir = Path.GetDirectoryName(binaryPath)!;
            var root = WalkUpToGameRoot(dir);
            var layout = ProbeKnownLayout(root);
            if (layout.Backend != UnityScriptBackend.Unknown)
            {
                return new Il2CppProjectProbeResult
                {
                    Backend = layout.Backend,
                    Il2CppBinaryPath = binaryPath,
                    MetadataPath = layout.MetadataPath,
                    ManagedDirectory = layout.ManagedDirectory,
                    AssetsDataDirectory = layout.AssetsDataDirectory,
                    Summary = layout.MetadataPath != null
                        ? "Binário IL2CPP válido."
                        : "Binário IL2CPP sem global-metadata (tente extrair do APK ou usar bypass)."
                };
            }

            var meta = FindMetadataFiles(root, MaxRecursiveSearchDepth).FirstOrDefault()
                       ?? FindMetadataFiles(dir, 3).FirstOrDefault();
            return new Il2CppProjectProbeResult
            {
                Backend = UnityScriptBackend.Il2Cpp,
                Il2CppBinaryPath = binaryPath,
                MetadataPath = meta,
                AssetsDataDirectory = FindAssetsDataDirectory(root),
                Summary = meta != null
                    ? "Binário IL2CPP válido."
                    : "Binário IL2CPP sem global-metadata (tente extrair do APK ou usar bypass)."
            };
        }

        if (string.Equals(fileName, "libunity.so", StringComparison.OrdinalIgnoreCase))
        {
            var libDir = Path.GetDirectoryName(binaryPath)!;
            var apkRoot = WalkUpToGameRoot(libDir);
            var probe = Probe(apkRoot);
            if (probe.Backend == UnityScriptBackend.Mono)
            {
                return probe;
            }

            return new Il2CppProjectProbeResult
            {
                Backend = UnityScriptBackend.Mono,
                Summary = "libunity.so pertence a Unity Mono/antigo — não use Il2CppDumper. " +
                          "Use assets/bin/Data/Managed no UABEANext."
            };
        }

        var parentDir = Path.GetDirectoryName(binaryPath);
        if (parentDir != null)
        {
            var parentProbe = Probe(parentDir);
            if (parentProbe.Backend != UnityScriptBackend.Unknown)
            {
                return parentProbe;
            }
        }

        return new Il2CppProjectProbeResult
        {
            Backend = UnityScriptBackend.Unknown,
            Il2CppBinaryPath = binaryPath,
            Summary = "Arquivo não reconhecido como libil2cpp nem GameAssembly."
        };
    }

    private static Il2CppProjectProbeResult ProbeKnownLayout(string root)
    {
        var assetsDataDir = FindAssetsDataDirectory(root);
        var managedDir = TryGetManagedDirectory(root, assetsDataDir);
        var metadataPath = TryGetMetadataPath(root, assetsDataDir);
        var il2cppBinary = TryGetIl2CppBinary(root);
        var hasLibMono = File.Exists(Path.Combine(root, "lib", "libmono.so")) ||
                         SafeEnumerateFiles(Path.Combine(root, "lib"), "libmono.so", 2).Any();
        var hasLibUnity = File.Exists(Path.Combine(root, "lib", "libunity.so")) ||
                          SafeEnumerateFiles(Path.Combine(root, "lib"), "libunity.so", 2).Any();

        if (!string.IsNullOrEmpty(il2cppBinary) && !string.IsNullOrEmpty(metadataPath))
        {
            var summary = "Projeto IL2CPP detectado (libil2cpp + global-metadata).";
            if (File.Exists(metadataPath))
            {
                var metaBytes = File.ReadAllBytes(metadataPath);
                if (BlockStrikeMetadataBypass.IsBlockStrikeEncrypted(metaBytes))
                {
                    summary = "IL2CPP Block Strike: global-metadata.dat criptografado — use dump Frida (eng/il2cpp/frida-dump-blockstrike-metadata.sh) ou global-metadata-decrypted.dat.";
                }
            }

            return new Il2CppProjectProbeResult
            {
                Backend = UnityScriptBackend.Il2Cpp,
                Il2CppBinaryPath = il2cppBinary,
                MetadataPath = metadataPath,
                AssetsDataDirectory = assetsDataDir,
                ManagedDirectory = managedDir,
                Summary = summary
            };
        }

        if (!string.IsNullOrEmpty(managedDir))
        {
            return new Il2CppProjectProbeResult
            {
                Backend = UnityScriptBackend.Mono,
                ManagedDirectory = managedDir,
                AssetsDataDirectory = assetsDataDir ?? Path.GetDirectoryName(managedDir),
                Summary = BuildMonoSummary(hasLibUnity, managedDir)
            };
        }

        if (hasLibUnity && string.IsNullOrEmpty(il2cppBinary))
        {
            return new Il2CppProjectProbeResult
            {
                Backend = UnityScriptBackend.Mono,
                ManagedDirectory = managedDir,
                AssetsDataDirectory = assetsDataDir,
                Summary = BuildMonoSummary(true, managedDir)
            };
        }

        if (!string.IsNullOrEmpty(metadataPath))
        {
            return new Il2CppProjectProbeResult
            {
                Backend = UnityScriptBackend.Il2Cpp,
                MetadataPath = metadataPath,
                Il2CppBinaryPath = il2cppBinary,
                Summary = !string.IsNullOrEmpty(il2cppBinary)
                    ? "Metadata IL2CPP encontrada; binário libil2cpp também presente."
                    : "Metadata IL2CPP encontrada, mas falta libil2cpp.so / GameAssembly."
            };
        }

        return Unknown("Layout Unity não reconhecido.");
    }

    private static Il2CppProjectProbeResult BuildProbeResult(
        List<string> managedDirs,
        List<string> metadataFiles,
        List<string> il2cppBins,
        bool hasLibMono,
        bool hasLibUnity,
        string? assetsDataDir)
    {
        if (il2cppBins.Count > 0 && metadataFiles.Count > 0)
        {
            var metaPath = metadataFiles[0];
            var summary = "Projeto IL2CPP detectado (libil2cpp + global-metadata).";
            if (File.Exists(metaPath))
            {
                var metaBytes = File.ReadAllBytes(metaPath);
                if (BlockStrikeMetadataBypass.IsBlockStrikeEncrypted(metaBytes))
                {
                    summary = "IL2CPP Block Strike: global-metadata.dat criptografado — use dump Frida (eng/il2cpp/frida-dump-blockstrike-metadata.sh) ou global-metadata-decrypted.dat.";
                }
            }

            return new Il2CppProjectProbeResult
            {
                Backend = UnityScriptBackend.Il2Cpp,
                Il2CppBinaryPath = il2cppBins[0],
                MetadataPath = metaPath,
                AssetsDataDirectory = assetsDataDir,
                ManagedDirectory = managedDirs.FirstOrDefault(),
                Summary = summary
            };
        }

        if (managedDirs.Count > 0 && (hasLibMono || managedDirs.Any(d => SafeGetFiles(d, "*.dll").Length > 0)))
        {
            var managed = managedDirs[0];
            return new Il2CppProjectProbeResult
            {
                Backend = UnityScriptBackend.Mono,
                ManagedDirectory = managed,
                AssetsDataDirectory = assetsDataDir ?? Path.GetDirectoryName(managed),
                Summary = BuildMonoSummary(hasLibUnity, managed)
            };
        }

        if (hasLibUnity && il2cppBins.Count == 0)
        {
            return new Il2CppProjectProbeResult
            {
                Backend = UnityScriptBackend.Mono,
                ManagedDirectory = managedDirs.FirstOrDefault(),
                AssetsDataDirectory = assetsDataDir,
                Summary = BuildMonoSummary(true, managedDirs.FirstOrDefault())
            };
        }

        if (metadataFiles.Count > 0)
        {
            return new Il2CppProjectProbeResult
            {
                Backend = UnityScriptBackend.Il2Cpp,
                MetadataPath = metadataFiles[0],
                Il2CppBinaryPath = il2cppBins.FirstOrDefault(),
                Summary = il2cppBins.Count > 0
                    ? "Metadata IL2CPP encontrada; binário libil2cpp também presente."
                    : "Metadata IL2CPP encontrada, mas falta libil2cpp.so / GameAssembly."
            };
        }

        return Unknown("Não foi possível detectar Mono nem IL2CPP nesta pasta.");
    }

    private static Il2CppProjectProbeResult Unknown(string summary) => new()
    {
        Backend = UnityScriptBackend.Unknown,
        Summary = summary
    };

    private static string BuildMonoSummary(bool hasLibUnity, string? managedDir)
    {
        var managedHint = managedDir != null
            ? $" Managed: {managedDir}"
            : "";
        return "Unity Mono detectado (Assembly-CSharp.dll, libmono.so) — Il2CppDumper não se aplica." +
               " Abra os .assets em assets/bin/Data no UABEANext; os templates MonoBehaviour vêm das DLLs." +
               (hasLibUnity ? " (libunity.so não é libil2cpp.)" : "") +
               managedHint;
    }

    private static string WalkUpToGameRoot(string startDir)
    {
        var dir = startDir;
        for (var i = 0; i < 8 && dir != null; i++)
        {
            if (LooksLikeUnityGameRoot(dir))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        return startDir;
    }

    private static bool LooksLikeUnityGameRoot(string root)
    {
        if (Directory.Exists(Path.Combine(root, "assets", "bin", "Data")))
            return true;
        if (Directory.Exists(Path.Combine(root, "lib")))
            return true;
        if (File.Exists(Path.Combine(root, "AndroidManifest.xml")))
            return true;
        if (Directory.Exists(Path.Combine(root, "Managed")) && SafeGetFiles(Path.Combine(root, "Managed"), "*.dll").Length > 0)
            return true;
        if (Directory.Exists(Path.Combine(root, "Metadata")))
            return true;
        if (Directory.Exists(Path.Combine(root, "il2cpp_data", "Metadata")))
            return true;

        return false;
    }

    private static bool IsBroadFilesystemRoot(string root)
    {
        var full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
        {
            var homeFull = Path.GetFullPath(home).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(full, homeFull, StringComparison.OrdinalIgnoreCase))
                return true;

            var homeParent = Path.GetDirectoryName(homeFull);
            if (homeParent != null && string.Equals(full, homeParent, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var downloads = Path.Combine(home, "Downloads");
        if (Directory.Exists(downloads) &&
            string.Equals(full, Path.GetFullPath(downloads), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return full == "/" ||
               string.Equals(full, "/home", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindAssetsDataDirectory(string root)
    {
        var direct = Path.Combine(root, "assets", "bin", "Data");
        if (Directory.Exists(direct))
        {
            return direct;
        }

        if (string.Equals(Path.GetFileName(root), "Data", StringComparison.OrdinalIgnoreCase))
        {
            return root;
        }

        return null;
    }

    private static string? TryGetManagedDirectory(string root, string? assetsDataDir)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrEmpty(assetsDataDir))
        {
            candidates.Add(Path.Combine(assetsDataDir, "Managed"));
        }

        candidates.Add(Path.Combine(root, "assets", "bin", "Data", "Managed"));
        candidates.Add(Path.Combine(root, "Managed"));

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate) && SafeGetFiles(candidate, "*.dll").Length > 0)
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? TryGetMetadataPath(string root, string? assetsDataDir)
    {
        var candidates = new List<string>();
        foreach (var name in MetadataFileNames)
        {
            candidates.Add(Path.Combine(root, "Metadata", name));
            candidates.Add(Path.Combine(root, "il2cpp_data", "Metadata", name));
            if (!string.IsNullOrEmpty(assetsDataDir))
            {
                candidates.Add(Path.Combine(assetsDataDir, "Metadata", name));
                candidates.Add(Path.Combine(assetsDataDir, "il2cpp_data", "Metadata", name));
            }
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? TryGetIl2CppBinary(string root)
    {
        foreach (var name in Il2CppBinaryNames)
        {
            var direct = Path.Combine(root, name);
            if (File.Exists(direct))
            {
                return direct;
            }
        }

        var libDir = Path.Combine(root, "lib");
        if (Directory.Exists(libDir))
        {
            foreach (var name in Il2CppBinaryNames)
            {
                var match = SafeEnumerateFiles(libDir, name, 2).FirstOrDefault();
                if (match != null)
                {
                    return match;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> FindManagedDirectories(string root, int maxDepth)
    {
        var direct = TryGetManagedDirectory(root, FindAssetsDataDirectory(root));
        if (direct != null)
        {
            yield return direct;
        }

        foreach (var dir in SafeEnumerateDirectories(root, "Managed", maxDepth))
        {
            if (SafeGetFiles(dir, "*.dll").Length > 0)
            {
                yield return dir;
            }
        }
    }

    private static IEnumerable<string> FindMetadataFiles(string root, int maxDepth)
    {
        var direct = TryGetMetadataPath(root, FindAssetsDataDirectory(root));
        if (direct != null)
        {
            yield return direct;
        }

        foreach (var name in MetadataFileNames)
        {
            foreach (var file in SafeEnumerateFiles(root, name, maxDepth))
            {
                yield return file;
            }
        }
    }

    private static IEnumerable<string> FindIl2CppBinaries(string root, int maxDepth)
    {
        var direct = TryGetIl2CppBinary(root);
        if (direct != null)
        {
            yield return direct;
        }

        foreach (var name in Il2CppBinaryNames)
        {
            foreach (var file in SafeEnumerateFiles(root, name, maxDepth))
            {
                yield return file;
            }
        }
    }

    private static readonly HashSet<string> SkipDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wine",
        ".cache",
        ".local",
        ".config",
        ".mozilla",
        ".steam",
        "proc",
        "sys",
        "dev",
        "run",
        "lost+found",
        "node_modules",
        ".git",
        ".nuget",
        "snap",
        "dosdevices",
    };

    private static bool ShouldSkipDirectory(string dirPath)
    {
        var name = Path.GetFileName(dirPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!string.IsNullOrEmpty(name) && SkipDirectoryNames.Contains(name))
        {
            return true;
        }

        return dirPath.Contains($"{Path.DirectorySeparatorChar}.wine{Path.DirectorySeparatorChar}dosdevices{Path.DirectorySeparatorChar}",
                   StringComparison.OrdinalIgnoreCase) ||
               dirPath.Contains($"{Path.AltDirectorySeparatorChar}.wine{Path.AltDirectorySeparatorChar}dosdevices{Path.AltDirectorySeparatorChar}",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string[] SafeGetFiles(string directory, string pattern)
    {
        try
        {
            return Directory.GetFiles(directory, pattern);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            return [];
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string root, string pattern, int maxDepth)
    {
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));

        while (pending.Count > 0)
        {
            var (current, depth) = pending.Pop();
            if (depth > maxDepth)
            {
                continue;
            }

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(current, pattern);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
            {
                continue;
            }

            foreach (var dir in directories)
            {
                if (ShouldSkipDirectory(dir))
                {
                    continue;
                }

                yield return dir;
                pending.Push((dir, depth + 1));
            }
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string root, string pattern, int maxDepth)
    {
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));

        while (pending.Count > 0)
        {
            var (current, depth) = pending.Pop();
            if (depth > maxDepth)
            {
                continue;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current, pattern);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
            {
                files = [];
            }

            foreach (var file in files)
            {
                yield return file;
            }

            if (depth >= maxDepth)
            {
                continue;
            }

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(current);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
            {
                continue;
            }

            foreach (var dir in directories)
            {
                if (!ShouldSkipDirectory(dir))
                {
                    pending.Push((dir, depth + 1));
                }
            }
        }
    }
}