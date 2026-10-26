using Microsoft.Win32;

namespace Sela.Services;

/// <summary>Start-with-Windows via HKCU\Software\Microsoft\Windows\CurrentVersion\Run ("Sela").</summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Sela";
    // Written by installer\sela.iss: 1 or 0 for the "Start with Windows" task.
    private const string SelaKey = @"Software\Sela";
    private const string InstallerValue = "InstallerAutostart";

    /// <summary>The installer's "Start with Windows" choice, or null for a portable copy.</summary>
    public static bool? InstallerChoice
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(SelaKey);
            return key?.GetValue(InstallerValue) is int v ? v != 0 : null;
        }
    }

    private static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string s && s.Length > 0;
        }
    }

    public static void Enable()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(ValueName, Command, RegistryValueKind.String);
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public static void Set(bool enabled)
    {
        if (enabled) Enable(); else Disable();
    }
}
