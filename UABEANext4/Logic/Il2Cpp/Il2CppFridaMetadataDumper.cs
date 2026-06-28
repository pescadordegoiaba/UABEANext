using Il2CppDumper.Protection;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace UABEANext4.Logic.Il2Cpp;

public static class Il2CppFridaMetadataDumper
{
    public const string BlockStrikePackage = "com.rexetstudio.blockstrike";

    public static bool TryDumpBlockStrikeMetadata(string? metadataPath, Action<string>? log, out string? decryptedPath)
    {
        decryptedPath = null;
        if (string.IsNullOrEmpty(metadataPath) || !File.Exists(metadataPath))
        {
            return false;
        }

        var bytes = File.ReadAllBytes(metadataPath);
        if (!BlockStrikeMetadataBypass.IsBlockStrikeEncrypted(bytes))
        {
            return false;
        }

        var outputPath = Path.Combine(
            Path.GetDirectoryName(metadataPath) ?? ".",
            "global-metadata-decrypted.dat");

        if (File.Exists(outputPath))
        {
            var existing = File.ReadAllBytes(outputPath);
            if (Il2CppProtectionBypass.IsValidMetadata(existing))
            {
                log?.Invoke($"Metadata descriptografado já existe: {outputPath}");
                decryptedPath = outputPath;
                return true;
            }
        }

        var scriptPath = ResolveFridaScriptPath();
        if (scriptPath == null)
        {
            log?.Invoke("Script Frida não encontrado (eng/il2cpp/frida-dump-blockstrike-metadata.js).");
            return false;
        }

        if (!TryFindAdb(log, out var adb))
        {
            log?.Invoke("adb não encontrado — conecte um dispositivo Android com USB debugging.");
            return false;
        }

        if (!TryFindPython(log, out var python))
        {
            log?.Invoke("python3 não encontrado para executar o dump Frida.");
            return false;
        }

        log?.Invoke("Tentando dump Frida de global-metadata (Block Strike)…");
        var psi = new ProcessStartInfo
        {
            FileName = python,
            WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? ".",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(scriptPath);
        psi.ArgumentList.Add("--adb");
        psi.ArgumentList.Add(adb);
        psi.ArgumentList.Add("--package");
        psi.ArgumentList.Add(BlockStrikePackage);
        psi.ArgumentList.Add("--output");
        psi.ArgumentList.Add(outputPath);
        psi.ArgumentList.Add("--size");
        psi.ArgumentList.Add(BlockStrikeMetadataBypass.ExpectedFileSize.ToString());

        try
        {
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                return false;
            }

            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(120_000);

            if (!string.IsNullOrWhiteSpace(stdout))
            {
                log?.Invoke(stdout.TrimEnd());
            }

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                log?.Invoke(stderr.TrimEnd());
            }

            if (proc.ExitCode != 0)
            {
                log?.Invoke($"Frida dump falhou (exit {proc.ExitCode}).");
                return false;
            }

            if (!File.Exists(outputPath) || !Il2CppProtectionBypass.IsValidMetadata(File.ReadAllBytes(outputPath)))
            {
                log?.Invoke("Dump Frida não produziu metadata válido.");
                return false;
            }

            decryptedPath = outputPath;
            log?.Invoke($"Metadata descriptografado salvo em: {outputPath}");
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"Frida dump erro: {ex.Message}");
            return false;
        }
    }

    private static string? ResolveFridaScriptPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "eng", "il2cpp", "frida-dump-blockstrike-metadata.py"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "eng", "il2cpp", "frida-dump-blockstrike-metadata.py")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "eng", "il2cpp", "frida-dump-blockstrike-metadata.py")),
            "/home/gullin/Projetos/UABEANext/eng/il2cpp/frida-dump-blockstrike-metadata.py",
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static bool TryFindAdb(Action<string>? log, out string adbPath)
    {
        adbPath = "adb";
        if (TryRun(adbPath, "version", log))
        {
            return true;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var sdk = Path.Combine(home, "Android", "Sdk", "platform-tools", "adb");
        if (File.Exists(sdk) && TryRun(sdk, "version", log))
        {
            adbPath = sdk;
            return true;
        }

        return false;
    }

    private static bool TryFindPython(Action<string>? log, out string pythonPath)
    {
        foreach (var name in new[] { "python3", "python" })
        {
            if (TryRun(name, "--version", log))
            {
                pythonPath = name;
                return true;
            }
        }

        pythonPath = "python3";
        return false;
    }

    private static bool TryRun(string file, string args, Action<string>? log)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var p = Process.Start(psi);
            if (p == null)
            {
                return false;
            }

            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}