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
        "新增：每个 Team 可单独配置 API Key，解决因权限不足导致部分文档页面无法同步的问题（设置 → Team 列表 → 独立 API Key 列）",
        "新增：更新后自动弹出更新日志，方便了解每次更新内容",
        "新增：设置中可选择「使用浏览器打开 Figma 链接」，解决桌面客户端打开后白屏的问题",
        "修复：同名 Page 在不同文档间互相覆盖导致搜索结果缺失",
        "修复：搜索结果数量上限从 16 条改为全量显示，不再遗漏匹配项",
        "修复：页面同步失败时增加自动重试（最多 3 次），并在同步完成后提示失败的文件名",
        "优化：对无法导出的文件（403）保留已有页面数据，避免反复清空",
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
