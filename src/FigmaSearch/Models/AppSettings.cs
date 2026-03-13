namespace FigmaSearch.Models;

public class AppSettings
{
    public string FigmaApiKey { get; set; } = string.Empty;
    public int UpdateIntervalHours { get; set; } = 48;
    public bool LaunchAtStartup { get; set; } = false;
    public string HotkeyConfig { get; set; } = "DoubleAlt";
    public DateTime? LastSyncTime { get; set; }
    public string LastUpdateCheckDate { get; set; } = string.Empty;
    public string AppVersion { get; set; } = "1.0.0";
    public bool IsFirstRun { get; set; } = true;
    public string Theme { get; set; } = "Dark"; // "Dark" | "Light"
}
