using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace InternetMonitor;

/// <summary>开机自动启动（写入 HKCU\...\Run，无需管理员权限）。</summary>
internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "InternetMonitor";

    private static string ExecutablePath
    {
        get
        {
            string? path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path))
                return path;

            return Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        }
    }

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

        if (enabled)
        {
            key.SetValue(ValueName, $"\"{ExecutablePath}\"");
            return;
        }

        if (key.GetValue(ValueName) != null)
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
