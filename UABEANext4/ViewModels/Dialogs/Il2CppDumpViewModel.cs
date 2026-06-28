using Avalonia.Platform.Storage;
using Il2CppDumper;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using UABEANext4.Interfaces;
using UABEANext4.Logic.Il2Cpp;

namespace UABEANext4.ViewModels.Dialogs;

public partial class Il2CppDumpViewModel : ViewModelBase, IDialogAware<bool?>
{
    [ObservableProperty]
    private string _il2CppBinaryPath = "";

    [ObservableProperty]
    private string _metadataPath = "";

    [ObservableProperty]
    private string _outputDirectory = "";

    [ObservableProperty]
    private string _gameDataDirectory = "";

    [ObservableProperty]
    private bool _installForWorkspace = true;

    [ObservableProperty]
    private bool _generateStruct = true;

    [ObservableProperty]
    private bool _generateDummyDll = true;

    [ObservableProperty]
    private bool _forceVersion;

    [ObservableProperty]
    private double _forceVersionValue = 24.3;

    [ObservableProperty]
    private bool _forceDump;

    [ObservableProperty]
    private string _logText = "";

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _probeStatus = "Selecione a pasta do APK extraído ou os binários IL2CPP.";

    [ObservableProperty]
    private bool _isMonoProject;

    public string Title => "Il2CppDumper (protected metadata bypass)";
    public int Width => 560;
    public int Height => 520;
    public bool IsModal => true;

    public event Action<bool?>? RequestClose;

    private IStorageProvider? _storageProvider;

    public void BindStorageProvider(IStorageProvider storageProvider) => _storageProvider = storageProvider;

    [RelayCommand]
    private async Task BrowseIl2CppAsync()
    {
        var path = await PickFileAsync("IL2CPP binary", [
            new("Native / WASM") { Patterns = ["*.dll", "*.so", "*.wasm", "*.*"] }
        ]);
        if (path != null)
        {
            path = path.Trim();
            Il2CppBinaryPath = path;
            ApplyProbe(Il2CppProjectProbe.ProbeNearBinary(path));
            if (string.IsNullOrWhiteSpace(OutputDirectory))
            {
                OutputDirectory = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path) ?? ".", "il2cpp_dump");
            }
        }
    }

    [RelayCommand]
    private async Task BrowseMetadataAsync()
    {
        var path = await PickFileAsync("global-metadata", [
            new("Metadata") { Patterns = ["global-metadata.dat", "*.dat", "*.*"] }
        ]);
        if (path != null)
        {
            MetadataPath = path.Trim();
        }
    }

    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        var path = await PickFolderAsync("Dump output folder");
        if (path != null)
        {
            OutputDirectory = path;
        }
    }

    [RelayCommand]
    private async Task BrowseGameDataAsync()
    {
        var path = await PickFolderAsync("Game Data folder (contains il2cpp_data)");
        if (path != null)
        {
            GameDataDirectory = path;
        }
    }

    [RelayCommand]
    private async Task BrowseApkRootAsync()
    {
        var path = await PickFolderAsync("APK extraído ou pasta do jogo");
        if (path == null)
        {
            return;
        }

        LastApkRoot = path.Trim();
        ApplyProbe(Il2CppProjectProbe.Probe(LastApkRoot));
    }

    public void ApplyProbe(Il2CppProjectProbeResult probe)
    {
        ProbeStatus = probe.Summary;
        IsMonoProject = probe.Backend == UnityScriptBackend.Mono;

        if (!string.IsNullOrEmpty(probe.AssetsDataDirectory))
        {
            GameDataDirectory = probe.AssetsDataDirectory;
        }

        if (probe.Backend == UnityScriptBackend.Il2Cpp)
        {
            if (!string.IsNullOrEmpty(probe.Il2CppBinaryPath))
            {
                Il2CppBinaryPath = probe.Il2CppBinaryPath;
            }

            if (!string.IsNullOrEmpty(probe.MetadataPath))
            {
                MetadataPath = probe.MetadataPath;
            }

            if (!string.IsNullOrEmpty(probe.Il2CppBinaryPath))
            {
                OutputDirectory = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(probe.Il2CppBinaryPath) ?? ".",
                    "il2cpp_dump");
            }
        }
    }

    public void BtnCancel_Click() => RequestClose?.Invoke(false);

    public async void BtnRun_Click()
    {
        if (IsMonoProject)
        {
            LogText = ProbeStatus;
            return;
        }

        if (!string.IsNullOrWhiteSpace(Il2CppBinaryPath))
        {
            ApplyProbe(Il2CppProjectProbe.ProbeNearBinary(Il2CppBinaryPath));
            if (IsMonoProject)
            {
                LogText = ProbeStatus;
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(Il2CppBinaryPath) || !System.IO.File.Exists(Il2CppBinaryPath))
        {
            LogText = "Selecione libil2cpp.so ou GameAssembly.dll (não libunity.so).";
            return;
        }

        IsRunning = true;
        LogText = "";
        try
        {
            var request = new Il2CppDumperRequest
            {
                Il2CppBinaryPath = Il2CppBinaryPath,
                MetadataPath = string.IsNullOrWhiteSpace(MetadataPath) ? null : MetadataPath,
                OutputDirectory = string.IsNullOrWhiteSpace(OutputDirectory) ? null : OutputDirectory,
                GenerateStruct = GenerateStruct,
                GenerateDummyDll = GenerateDummyDll,
                ForceVersion = ForceVersion,
                ForceVersionValue = ForceVersionValue,
                ForceDump = ForceDump,
            };

            var lines = new ObservableCollection<string>();
            var result = await Task.Run(() => Logic.Il2Cpp.Il2CppDumpService.RunDump(request, line =>
            {
                lines.Add(line);
            }));

            LogText = result.Log;
            if (!result.Success)
            {
                LogText += Environment.NewLine + (result.ErrorMessage ?? "Dump failed.");
                return;
            }

            LastResult = result;
            RequestClose?.Invoke(true);
        }
        finally
        {
            IsRunning = false;
        }
    }

    public Il2CppDumperResult? LastResult { get; private set; }

    public bool RegisteredMono { get; private set; }

    public string? LastApkRoot { get; private set; }

    public void BtnUseMono_Click()
    {
        var roots = new[]
        {
            LastApkRoot,
            GameDataDirectory,
            string.IsNullOrWhiteSpace(Il2CppBinaryPath) ? null : System.IO.Path.GetDirectoryName(Il2CppBinaryPath)
        };

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            var probe = Il2CppProjectProbe.Probe(root);
            ApplyProbe(probe);
            if (probe.Backend == UnityScriptBackend.Mono && !string.IsNullOrEmpty(probe.ManagedDirectory))
            {
                RegisteredMono = true;
                RequestClose?.Invoke(false);
                return;
            }
        }

        LogText = "Não encontrei pasta Managed com DLLs. Aponte a pasta do APK extraído (Detectar na pasta…).";
    }

    private async Task<string?> PickFileAsync(string title, FilePickerFileType[] types)
    {
        if (_storageProvider == null)
        {
            return null;
        }

        var files = await _storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = types
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private async Task<string?> PickFolderAsync(string title)
    {
        if (_storageProvider == null)
        {
            return null;
        }

        var folders = await _storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}