using Il2CppDumper.Protection;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Il2CppDumper;

public sealed class Il2CppDumperRequest
{
    public required string Il2CppBinaryPath { get; init; }
    public string? MetadataPath { get; init; }
    public string? OutputDirectory { get; init; }
    public bool GenerateStruct { get; init; } = true;
    public bool GenerateDummyDll { get; init; } = true;
    public bool ForceVersion { get; init; }
    public double ForceVersionValue { get; init; } = 24.3;
    public bool ForceDump { get; init; }
    public int MachoPlatformIndex { get; init; } = -1;
}

public sealed class Il2CppDumperResult
{
    public bool Success { get; init; }
    public string? OutputDirectory { get; init; }
    public string? MetadataPathUsed { get; init; }
    public string? Il2CppBinaryPathUsed { get; init; }
    public string? MetadataBypassMethod { get; init; }
    public string? BinaryBypassMethod { get; init; }
    public string? ErrorMessage { get; init; }
    public string Log { get; init; } = "";
}

public static class Il2CppDumperApi
{
    private static readonly string[] Il2CppBinaryNames =
    [
        "libil2cpp.so",
        "libil2cpp.dbg.so",
        "GameAssembly.dll",
        "GameAssembly.so",
    ];

    private static bool IsKnownUnityMonoBinary(string fileName) =>
        string.Equals(fileName, "libunity.so", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(fileName, "libmono.so", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(fileName, "libmain.so", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeIl2CppElf(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> header = stackalloc byte[64];
            if (fs.Read(header) < 16)
            {
                return false;
            }

            if (header[0] != 0x7F || header[1] != (byte)'E')
            {
                return false;
            }

            var bytes = File.ReadAllBytes(path);
            return Il2CppProtectionBypass.TryExtractMetadataFromBinary(bytes, 1) != null;
        }
        catch
        {
            return false;
        }
    }

    public static Il2CppDumperResult Run(Il2CppDumperRequest request, Action<string>? log = null)
    {
        void Write(string line)
        {
            log?.Invoke(line);
        }

        var logWriter = new StringWriter();
        void Log(string line)
        {
            logWriter.WriteLine(line);
            Write(line);
        }

        try
        {
            if (!File.Exists(request.Il2CppBinaryPath))
            {
                return Fail($"IL2CPP binary not found: {request.Il2CppBinaryPath}", logWriter);
            }

            var fileName = Path.GetFileName(request.Il2CppBinaryPath);
            if (IsKnownUnityMonoBinary(fileName))
            {
                return Fail(
                    $"{fileName} não é IL2CPP (Unity Mono/antigo). Use Managed/*.dll no UABEANext — " +
                    "Il2CppDumper exige libil2cpp.so ou GameAssembly + global-metadata.dat.",
                    logWriter);
            }

            if (!Il2CppBinaryNames.Contains(fileName, StringComparer.OrdinalIgnoreCase) &&
                !LooksLikeIl2CppElf(request.Il2CppBinaryPath))
            {
                Log($"WARNING: '{fileName}' não é um nome IL2CPP usual; tentando mesmo assim...");
            }

            var outputDir = request.OutputDirectory;
            if (string.IsNullOrWhiteSpace(outputDir))
            {
                outputDir = Path.Combine(Path.GetDirectoryName(request.Il2CppBinaryPath) ?? ".", "il2cpp_dump");
            }

            Directory.CreateDirectory(outputDir);

            var il2cppBytes = File.ReadAllBytes(request.Il2CppBinaryPath);
            il2cppBytes = Il2CppProtectionBypass.PrepareIl2CppBinary(il2cppBytes, out var binaryBypass);
            if (binaryBypass != null && binaryBypass != "none")
            {
                Log($"IL2CPP binary bypass: {binaryBypass}");
            }

            string? metadataPath = request.MetadataPath;
            byte[]? metadataBytes = null;
            string? metadataBypass = null;

            if (!string.IsNullOrEmpty(metadataPath) && File.Exists(metadataPath))
            {
                metadataBytes = File.ReadAllBytes(metadataPath);
                metadataBytes = Il2CppProtectionBypass.PrepareMetadata(metadataBytes, metadataPath, out metadataBypass);
                if (metadataBypass == null)
                {
                    Log("WARNING: Metadata still looks invalid after bypass attempts.");
                }
                else if (metadataBypass != "none")
                {
                    Log($"Metadata bypass: {metadataBypass}");
                }
            }
            else
            {
                Log("Metadata file not provided; searching inside IL2CPP binary...");
                metadataBytes = Il2CppProtectionBypass.TryExtractMetadataFromBinary(il2cppBytes);
                metadataBypass = metadataBytes != null ? "extracted-from-binary" : null;
                if (metadataBytes == null)
                {
                    return Fail("Metadata file not found or could not be decrypted/extracted.", logWriter);
                }
            }

            if (metadataBytes == null || !Il2CppProtectionBypass.IsValidMetadata(metadataBytes))
            {
                Log("Trying metadata extraction from IL2CPP binary as fallback...");
                var extracted = Il2CppProtectionBypass.TryExtractMetadataFromBinary(il2cppBytes);
                if (extracted != null)
                {
                    metadataBytes = extracted;
                    metadataBypass = "extracted-from-binary-fallback";
                    Log($"Metadata fallback: {metadataBypass}");
                }
                else
                {
                    if (BlockStrikeMetadataBypass.IsBlockStrikeEncrypted(metadataBytes))
                    {
                        return Fail(BlockStrikeMetadataBypass.BuildFridaHint(metadataPath), logWriter);
                    }

                    return Fail("Invalid or encrypted global-metadata (protection bypass failed).", logWriter);
                }
            }

            var preparedMetaPath = Path.Combine(outputDir, "global-metadata-uabea.dat");
            File.WriteAllBytes(preparedMetaPath, metadataBytes);
            metadataPath = preparedMetaPath;

            if (!ProgramRunner.Init(
                    il2cppBytes,
                    metadataBytes,
                    request,
                    Log,
                    out var metadata,
                    out var il2Cpp))
            {
                return Fail("Il2CppDumper initialization failed (protected binary or unknown layout).", logWriter);
            }

            ProgramRunner.Dump(metadata, il2Cpp, outputDir, Log, request.GenerateStruct, request.GenerateDummyDll);

            return new Il2CppDumperResult
            {
                Success = true,
                OutputDirectory = outputDir,
                MetadataPathUsed = metadataPath,
                Il2CppBinaryPathUsed = request.Il2CppBinaryPath,
                MetadataBypassMethod = metadataBypass,
                BinaryBypassMethod = binaryBypass,
                Log = logWriter.ToString()
            };
        }
        catch (Exception ex)
        {
            Log($"ERROR: {ex}");
            return new Il2CppDumperResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Log = logWriter.ToString()
            };
        }
    }

    private static Il2CppDumperResult Fail(string message, StringWriter logWriter) =>
        new()
        {
            Success = false,
            ErrorMessage = message,
            Log = logWriter.ToString()
        };

    /// <summary>
    /// Internal runner extracted from upstream Program.cs (library build excludes Program.Main).
    /// </summary>
    internal static class ProgramRunner
    {
        private static readonly Config Config = LoadConfig();

        private static Config LoadConfig()
        {
            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            if (File.Exists(configPath))
            {
                return JsonSerializer.Deserialize<Config>(File.ReadAllText(configPath)) ?? new Config();
            }

            return new Config { RequireAnyKey = false };
        }

        public static bool Init(
            byte[] il2cppBytes,
            byte[] metadataBytes,
            Il2CppDumperRequest request,
            Action<string> log,
            out Metadata metadata,
            out Il2Cpp il2Cpp)
        {
            ApplyRequestToConfig(request);

            log("Initializing metadata...");
            metadata = new Metadata(new MemoryStream(metadataBytes));
            log($"Metadata Version: {metadata.Version}");

            log("Initializing il2cpp file...");
            il2cppBytes = Il2CppProtectionBypass.PrepareIl2CppBinary(il2cppBytes, out _);
            var il2cppMagic = BitConverter.ToUInt32(il2cppBytes, 0);
            var il2CppMemory = new MemoryStream(il2cppBytes);
            switch (il2cppMagic)
            {
                default:
                    throw new NotSupportedException("ERROR: il2cpp file not supported.");
                case 0x6D736100:
                    var web = new WebAssembly(il2CppMemory);
                    il2Cpp = web.CreateMemory();
                    break;
                case 0x304F534E:
                    var nso = new NSO(il2CppMemory);
                    il2Cpp = nso.UnCompress();
                    break;
                case 0x905A4D:
                    il2Cpp = new PE(il2CppMemory);
                    break;
                case 0x464c457f:
                    if (il2cppBytes[4] == 2)
                    {
                        il2Cpp = new Elf64(il2CppMemory);
                    }
                    else
                    {
                        il2Cpp = new Elf(il2CppMemory);
                    }
                    break;
                case 0xCAFEBABE:
                case 0xBEBAFECA:
                    var machofat = new MachoFat(new MemoryStream(il2cppBytes));
                    var index = request.MachoPlatformIndex;
                    if (index < 0 || index >= machofat.fats.Length)
                    {
                        index = 0;
                    }

                    var magic = machofat.fats[index].magic;
                    il2cppBytes = machofat.GetMacho(index);
                    il2CppMemory = new MemoryStream(il2cppBytes);
                    il2Cpp = magic == 0xFEEDFACF ? new Macho64(il2CppMemory) : new Macho(il2CppMemory);
                    break;
                case 0xFEEDFACF:
                    il2Cpp = new Macho64(il2CppMemory);
                    break;
                case 0xFEEDFACE:
                    il2Cpp = new Macho(il2CppMemory);
                    break;
            }

            var version = request.ForceVersion ? request.ForceVersionValue : metadata.Version;
            il2Cpp.SetProperties(version, metadata.metadataUsagesCount);

            log("Searching...");
            try
            {
                if (!il2Cpp.Search())
                {
                    if (!il2Cpp.SymbolSearch())
                    {
                        log("ERROR: Can't use auto mode to process file, try manual mode.");
                        return false;
                    }
                }
            }
            catch (Exception e)
            {
                log(e.ToString());
                return false;
            }

            if (il2Cpp.Version >= 27 && il2Cpp.IsDumped)
            {
                log("ERROR: This file is dumped, you don't need to use Il2CppDumper.");
                return false;
            }

            log("Done.");
            return true;
        }

        public static void Dump(Metadata metadata, Il2Cpp il2Cpp, string outputDir, Action<string> log, bool generateStruct, bool generateDummyDll)
        {
            log("Dumping...");
            var executor = new Il2CppExecutor(metadata, il2Cpp);
            var decompiler = new Il2CppDecompiler(executor);
            decompiler.Decompile(Config, outputDir);
            log("Done!");

            if (generateStruct)
            {
                log("Generate struct...");
                var scriptGenerator = new StructGenerator(executor);
                scriptGenerator.WriteScript(outputDir);
                log("Done!");
            }

            if (generateDummyDll)
            {
                log("Generate dummy dll...");
                DummyAssemblyExporter.Export(executor, outputDir, Config.DummyDllAddToken);
                log("Done!");
            }
        }

        private static void ApplyRequestToConfig(Il2CppDumperRequest request)
        {
            Config.ForceIl2CppVersion = request.ForceVersion;
            Config.ForceVersion = request.ForceVersionValue;
            Config.ForceDump = request.ForceDump;
            Config.GenerateStruct = request.GenerateStruct;
            Config.GenerateDummyDll = request.GenerateDummyDll;
        }
    }
}