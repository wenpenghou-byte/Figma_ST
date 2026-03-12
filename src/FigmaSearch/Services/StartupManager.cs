using Microsoft.Win32;

namespace FigmaSearch.Services;

public static class StartupManager
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "FigmaSearch";

    public static void AddToStartup()
    {
        var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.SetValue(AppName, $"\"{exe}\"");
    }

    public static void RemoveFromStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(AppName) != null)
            key.DeleteValue(AppName);
    }

    public static bool IsInStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(AppName) != null;
    }

    public static void Set(bool enable)
    {
        if (enable) AddToStartup(); else RemoveFromStartup();
    }
}
