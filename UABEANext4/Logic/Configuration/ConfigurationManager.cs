using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using UABEANext4.Util;

namespace UABEANext4.Logic.Configuration;
public static class ConfigurationManager
{
    public const string CONFIG_FILENAME = "config.json";
    public static ConfigurationValues Settings { get; }
    public static bool IsInitialized { get; }
    public static string ConfigPath { get; } = GetConfigPath();

    private static readonly JsonSerializerOptions OPTIONS = new()
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    static ConfigurationManager()
    {
        var configPath = GetReadableConfigPath();
        if (!File.Exists(configPath))
        {
            Settings = new ConfigurationValues();
            IsInitialized = true;
        }
        else
        {
            var configText = File.ReadAllText(configPath);
            Settings = JsonSerializer.Deserialize<ConfigurationValues>(configText, OPTIONS) 
                ?? new ConfigurationValues();
            
            IsInitialized = true;
        }
    }

    public static void SaveConfig()
    {
        if (!IsInitialized)
            return;

        try
        {
            var configDir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

            var configText = JsonSerializer.Serialize(Settings, OPTIONS);
            File.WriteAllText(ConfigPath, configText);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Failed to save config to {ConfigPath}: {ex.Message}");
        }
    }

    private static string GetReadableConfigPath()
    {
        if (File.Exists(ConfigPath))
        {
            return ConfigPath;
        }

        var legacyConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CONFIG_FILENAME);
        if (!StringComparer.Ordinal.Equals(ConfigPath, legacyConfigPath) && File.Exists(legacyConfigPath))
        {
            return legacyConfigPath;
        }

        return ConfigPath;
    }

    private static string GetConfigPath()
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrWhiteSpace(configHome))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (string.IsNullOrWhiteSpace(home))
                {
                    home = Environment.GetEnvironmentVariable("HOME");
                }

                configHome = string.IsNullOrWhiteSpace(home)
                    ? AppDomain.CurrentDomain.BaseDirectory
                    : Path.Combine(home, ".config");
            }

            return Path.Combine(configHome, "uabea-next", CONFIG_FILENAME);
        }

        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CONFIG_FILENAME);
    }
}
