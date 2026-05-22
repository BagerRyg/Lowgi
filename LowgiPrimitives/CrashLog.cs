using System;
using System.IO;
using System.Threading;
using Microsoft.Win32;

namespace LowgiPrimitives;

public static class CrashLog
{
    private static int _writeCount;
    private static Timer? _heartbeatTimer;

    public static bool Enabled { get; private set; }

    public static void SetEnabled(bool enabled, string executableName = "Lowgi.exe")
    {
        Enabled = enabled;

        if (enabled)
        {
            ConfigureNativeCrashDumps(executableName);
            StartHeartbeat();
            WriteRunEvent("logging enabled");
        }
        else
        {
            StopHeartbeat();
            DisableNativeCrashDumps(executableName);
        }
    }

    public static string LogDirectory
    {
        get
        {
            string directory = Path.Combine(AppContext.BaseDirectory, "crash_logs");
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    public static void ConfigureNativeCrashDumps(string executableName)
    {
        if (!Enabled)
        {
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            string keyPath = $@"Software\Microsoft\Windows\Windows Error Reporting\LocalDumps\{executableName}";
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath);
            key.SetValue("DumpFolder", LogDirectory, RegistryValueKind.ExpandString);
            key.SetValue("DumpCount", 10, RegistryValueKind.DWord);
            key.SetValue("DumpType", 2, RegistryValueKind.DWord);
            WriteRunEvent($"native dumps enabled for {executableName}");
        }
        catch (Exception ex)
        {
            Write(ex, "ConfigureNativeCrashDumps");
        }
    }

    public static void DisableNativeCrashDumps(string executableName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            string keyPath = $@"Software\Microsoft\Windows\Windows Error Reporting\LocalDumps\{executableName}";
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, false);
        }
        catch
        {
        }
    }

    public static void StartHeartbeat()
    {
        if (!Enabled)
        {
            return;
        }

        _heartbeatTimer ??= new Timer(_ => WriteHeartbeat(), null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }

    public static void StopHeartbeat()
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
    }

    public static void Write(Exception exception, string source)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            long unixTime = DateTimeOffset.Now.ToUnixTimeSeconds();
            int count = Interlocked.Increment(ref _writeCount);
            string path = Path.Combine(LogDirectory, $"crashlog_{unixTime}_{count}.log");

            using StreamWriter writer = new(path, false);
            writer.WriteLine($"Time: {DateTimeOffset.Now:O}");
            writer.WriteLine($"Source: {source}");
            writer.WriteLine($"AppBase: {AppContext.BaseDirectory}");
            writer.WriteLine($"OS: {Environment.OSVersion}");
            writer.WriteLine($"Runtime: {Environment.Version}");
            writer.WriteLine();
            writer.WriteLine(exception);
        }
        catch
        {
        }
    }

    public static void WriteRunEvent(string message)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            File.AppendAllText(
                Path.Combine(LogDirectory, "run.log"),
                $"{DateTimeOffset.Now:O}\t{message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private static void WriteHeartbeat()
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            File.WriteAllText(
                Path.Combine(LogDirectory, "heartbeat.txt"),
                $"LastHeartbeat: {DateTimeOffset.Now:O}{Environment.NewLine}ProcessId: {Environment.ProcessId}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
