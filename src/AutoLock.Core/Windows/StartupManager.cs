using Microsoft.Win32;
using System.Diagnostics;
using System.Reflection;

namespace AutoLock.Core;

public static class StartupManager
{
    private const string AppName = "AutoLock";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(AppName) is string value && IsCurrentAppCommand(value);
    }

    public static void SetEnabled(bool enabled, bool startInBackground)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (enabled)
        {
            key.SetValue(AppName, BuildStartupCommand(startInBackground), RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
        }
    }

    private static string BuildStartupCommand(bool startInBackground)
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = Process.GetCurrentProcess().MainModule?.FileName;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            path = Assembly.GetEntryAssembly()?.Location ?? "AutoLock.exe";
        }

        return startInBackground ? $"\"{path}\" --background" : $"\"{path}\"";
    }

    private static bool IsCurrentAppCommand(string value)
    {
        var current = BuildStartupCommand(startInBackground: false);
        var background = BuildStartupCommand(startInBackground: true);
        return string.Equals(value, current, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, background, StringComparison.OrdinalIgnoreCase);
    }
}
