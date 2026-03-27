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
        "优化：设置页面 Team 列表改版——输入框直接显示深色底，所见即所得；「添加」按钮改为横跨整行的「＋ 添加一行」，点击新增空行后直接填写即可，无需再点写入",
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
