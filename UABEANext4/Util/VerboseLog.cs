using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace UABEANext4.Util;

/// <summary>
/// Application-wide verbose trace log (file + stderr).
/// Enabled by default; disable with env UABEA_VERBOSE=0 or config VerboseLogging=false.
/// </summary>
public static class VerboseLog
{
    private static readonly object Sync = new();
    private static StreamWriter? _writer;
    private static bool _initialized;
    private static bool _enabled = true;

    public static bool Enabled => _enabled;
    public static string? LogFilePath { get; private set; }

    public static void InitializeEarly(string[]? args = null)
    {
        lock (Sync)
        {
            if (_initialized)
            {
                return;
            }

            _enabled = ReadEnabledFromEnvironment();
            LogFilePath = GetLogFilePath();
            EnsureWriter();

            _initialized = true;
            WriteHeader(args);
        }
    }

    public static void ApplyConfiguration(bool verboseLogging)
    {
        lock (Sync)
        {
            if (!ReadEnabledFromEnvironment())
            {
                _enabled = verboseLogging;
            }

            Log("Config", $"VerboseLogging config value={verboseLogging}, effective enabled={_enabled}");
        }
    }

    public static void Log(
        string category,
        string message,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        if (!_enabled)
        {
            return;
        }

        var fileName = Path.GetFileName(file);
        var lineText = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Thread.CurrentThread.ManagedThreadId}] [{category}] {member} ({fileName}:{line}): {message}";
        WriteLine(lineText);
    }

    public static void LogException(
        string category,
        Exception ex,
        string? message = null,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        var detail = message is null ? ex.ToString() : $"{message}{Environment.NewLine}{ex}";
        Log(category, detail, member, file, line);
    }

    public static VerboseLogScope Scope(string category, string operation, string? details = null)
        => new(category, operation, details);

    private static bool ReadEnabledFromEnvironment()
    {
        var env = Environment.GetEnvironmentVariable("UABEA_VERBOSE");
        if (string.IsNullOrWhiteSpace(env))
        {
            return true;
        }

        return env is not "0" and not "false" and not "False" and not "no" and not "NO";
    }

    private static string GetLogFilePath()
    {
        string baseDir;
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrWhiteSpace(configHome))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (string.IsNullOrWhiteSpace(home))
                {
                    home = Environment.GetEnvironmentVariable("HOME") ?? AppDomain.CurrentDomain.BaseDirectory;
                }

                configHome = Path.Combine(home, ".config");
            }

            baseDir = Path.Combine(configHome, "uabea-next");
        }
        else
        {
            baseDir = AppDomain.CurrentDomain.BaseDirectory;
        }

        return Path.Combine(baseDir, "verbose.log");
    }

    private static void EnsureWriter()
    {
        if (LogFilePath is null)
        {
            return;
        }

        var dir = Path.GetDirectoryName(LogFilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _writer?.Dispose();
        _writer = new StreamWriter(LogFilePath, append: true, Encoding.UTF8)
        {
            AutoFlush = true
        };
    }

    private static void WriteHeader(string[]? args)
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== UABEANext session start ==========");
        sb.Append($"Time: {DateTime.Now:O}{Environment.NewLine}");
        sb.Append($"PID: {Environment.ProcessId}{Environment.NewLine}");
        sb.Append($"Version: {Environment.Version}{Environment.NewLine}");
        sb.Append($"OS: {Environment.OSVersion}{Environment.NewLine}");
        sb.Append($"BaseDirectory: {AppDomain.CurrentDomain.BaseDirectory}{Environment.NewLine}");
        sb.Append($"LogFile: {LogFilePath}{Environment.NewLine}");
        sb.Append($"Args: {(args is null ? "(none)" : string.Join(' ', args))}{Environment.NewLine}");
        WriteLine(sb.ToString().TrimEnd());
    }

    private static void WriteLine(string line)
    {
        lock (Sync)
        {
            try
            {
                Console.Error.WriteLine(line);
            }
            catch
            {
                // ignore console failures (no tty)
            }

            try
            {
                _writer?.WriteLine(line);
            }
            catch
            {
                // ignore file failures
            }

            Debug.WriteLine(line);
        }
    }

    public sealed class VerboseLogScope : IDisposable
    {
        private readonly string _category;
        private readonly string _operation;
        private readonly Stopwatch _sw;
        private bool _finished;

        public VerboseLogScope(string category, string operation, string? details)
        {
            _category = category;
            _operation = operation;
            _sw = Stopwatch.StartNew();
            _finished = false;
            Log(category, $"BEGIN {operation}" + (details is null ? "" : $" | {details}"));
        }

        public void Complete(string? details = null)
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            _sw.Stop();
            Log(_category, $"END {_operation} ({_sw.ElapsedMilliseconds} ms)" + (details is null ? "" : $" | {details}"));
        }

        public void Fail(Exception ex, string? details = null)
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            _sw.Stop();
            LogException(_category, ex, $"FAIL {_operation} ({_sw.ElapsedMilliseconds} ms)" + (details is null ? "" : $" | {details}"));
        }

        public void Fail(string message)
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            _sw.Stop();
            Log(_category, $"FAIL {_operation} ({_sw.ElapsedMilliseconds} ms) | {message}");
        }

        public void Dispose()
        {
            if (!_finished)
            {
                Complete();
            }
        }
    }
}