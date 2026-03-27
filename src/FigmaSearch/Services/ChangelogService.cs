namespace FigmaSearch.Services;

/// <summary>
/// Manages the "What's New" changelog shown once per version after update.
/// To add notes for a new release, edit the <see cref="Notes"/> list below.
/// </summary>
public static class ChangelogService
{
    /// <summary>
    /// Release notes for the current version. Shown in a dialog once after update.
    /// Clear the list and add new items each release. If empty, the default
    /// "修复了一些已知的 bug" message is shown.
    /// </summary>
    public static readonly List<string> Notes = new()
    {
        "优化：设置页面 Team IDs 改版——每行直接显示为可编辑输入框，无需「写入」按钮；删除和上下移动按钮直接在每行后方，无需先选中",
        "优化：设置页面 Figma API Key 的帮助按钮（?）移至标题旁，与 Team IDs 风格一致",
    };

    /// <summary>
    /// Shows a changelog dialog if the current version differs from the last
    /// shown version. Updates the stored version after showing.
    /// </summary>
    public static void ShowIfUpdated(DatabaseService db)
    {
        var currentVersion = UpdateService.CurrentVersion();
        var lastShownVersion = db.GetSetting("last_changelog_version") ?? "";

        if (lastShownVersion == currentVersion) return;

        // Build changelog text
        var lines = new List<string> { $"肥姑妈搜已更新到 v{currentVersion}\n" };

        if (Notes.Count > 0)
        {
            foreach (var note in Notes)
                lines.Add($"• {note}");
        }
        else
        {
            lines.Add("• 修复了一些已知的 bug");
        }

        lines.Add("");
        lines.Add("bug 反馈联系：houwenpeng@corp.netease.com");

        var message = string.Join("\n", lines);

        System.Windows.MessageBox.Show(
            message,
            "更新日志",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);

        // Mark this version as shown
        db.SetSetting("last_changelog_version", currentVersion);
    }
}
