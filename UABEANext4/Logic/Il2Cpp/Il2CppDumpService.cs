using Il2CppDumper;
using System;
using System.IO;
using UABEANext4.Util;

namespace UABEANext4.Logic.Il2Cpp;

public static class Il2CppDumpService
{
    public static Il2CppDumperResult RunDump(Il2CppDumperRequest request, Action<string>? log = null)
    {
        using var scope = VerboseLog.Scope("Il2CppDump", "Run");

        request = NormalizeRequest(request);
        var probe = Il2CppProjectProbe.ProbeNearBinary(request.Il2CppBinaryPath);
        if (probe.Backend == UnityScriptBackend.Mono)
        {
            VerboseLog.Log("Il2CppDump", probe.Summary);
            return new Il2CppDumperResult
            {
                Success = false,
                ErrorMessage = probe.Summary,
                Log = probe.Summary
            };
        }

        if (!string.IsNullOrEmpty(request.MetadataPath) && File.Exists(request.MetadataPath))
        {
            var metaProbe = Il2CppProjectProbe.Probe(request.MetadataPath);
            if (metaProbe.Backend == UnityScriptBackend.Mono)
            {
                return MonoFail(metaProbe.Summary);
            }
        }

        var result = Il2CppDumperApi.Run(request, line =>
        {
            log?.Invoke(line);
            VerboseLog.Log("Il2CppDump", line);
        });

        if (!result.Success &&
            !string.IsNullOrEmpty(request.MetadataPath) &&
            Il2CppFridaMetadataDumper.TryDumpBlockStrikeMetadata(request.MetadataPath, log, out var decrypted))
        {
            request = new Il2CppDumperRequest
            {
                Il2CppBinaryPath = request.Il2CppBinaryPath,
                MetadataPath = decrypted,
                OutputDirectory = request.OutputDirectory,
                GenerateStruct = request.GenerateStruct,
                GenerateDummyDll = request.GenerateDummyDll,
                ForceVersion = request.ForceVersion,
                ForceVersionValue = request.ForceVersionValue,
                ForceDump = request.ForceDump,
                MachoPlatformIndex = request.MachoPlatformIndex,
            };
            result = Il2CppDumperApi.Run(request, line =>
            {
                log?.Invoke(line);
                VerboseLog.Log("Il2CppDump", line);
            });
        }

        if (!result.Success && probe.Backend == UnityScriptBackend.Unknown)
        {
            var rootProbe = Il2CppProjectProbe.Probe(Path.GetDirectoryName(request.Il2CppBinaryPath) ?? ".");
            if (rootProbe.Backend == UnityScriptBackend.Mono)
            {
                return MonoFail(rootProbe.Summary + Environment.NewLine + (result.ErrorMessage ?? ""));
            }
        }

        scope.Complete(result.Success ? "ok" : result.ErrorMessage ?? "failed");
        return result;
    }

    private static Il2CppDumperRequest NormalizeRequest(Il2CppDumperRequest request)
    {
        var binary = request.Il2CppBinaryPath.Trim();
        var meta = request.MetadataPath?.Trim();
        var output = request.OutputDirectory?.Trim();
        return new Il2CppDumperRequest
        {
            Il2CppBinaryPath = binary,
            MetadataPath = string.IsNullOrEmpty(meta) ? null : meta,
            OutputDirectory = string.IsNullOrEmpty(output) ? null : output,
            GenerateStruct = request.GenerateStruct,
            GenerateDummyDll = request.GenerateDummyDll,
            ForceVersion = request.ForceVersion,
            ForceVersionValue = request.ForceVersionValue,
            ForceDump = request.ForceDump,
            MachoPlatformIndex = request.MachoPlatformIndex,
        };
    }

    private static Il2CppDumperResult MonoFail(string message) =>
        new()
        {
            Success = false,
            ErrorMessage = message,
            Log = message
        };
}