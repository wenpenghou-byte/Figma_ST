using FigmaSearch.Models;
using FigmaSearch.Services;
using FigmaSearch.ViewModels;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FigmaSearch.Windows;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;
    private bool _isRecordingHotkey;

    public SettingsWindow()
    {
        InitializeComponent();
        _vm = new SettingsViewModel(App.DB);
        DataContext = _vm;
        TeamsGrid.ItemsSource = _vm.Teams;
        ApiKeyBox.Password = _vm.FigmaApiKey;
        IntervalSlider.Value = _vm.UpdateIntervalHours;
        IntervalLabel.Text = $"{_vm.UpdateIntervalHours} 小时";
        StartupCheck.IsChecked = _vm.LaunchAtStartup;
        HotkeyBox.Text = FormatConfigForDisplay(_vm.HotkeyConfig);
        // Set theme combo selection without triggering the change handler
        ThemeCombo.SelectionChanged -= ThemeCombo_Changed;
        ThemeCombo.SelectedIndex = _vm.Theme == "Light" ? 1 : 0;
        ThemeCombo.SelectionChanged += ThemeCombo_Changed;
        VersionLabel.Text = $"当前版本：{UpdateService.CurrentVersion()}";
        UpdateDbStats();

        // BrainMaker AI fields
        BmAuthAccountBox.Text = _vm.BmAuthAccount;
        BmAuthKeyBox.Password = _vm.BmAuthKey;
        BmUserCorpBox.Text = _vm.BmUserCorp;
        BmProjectBox.Text = _vm.BmProject;
        BmDocsetBox.Text = _vm.BmDocset;

        OpenInBrowserCheck.IsChecked = _vm.OpenInBrowser;

        // If there's already a pending update, show install button immediately
        if (App.PendingUpdate != null)
            ShowInstallButton($"发现新版本 v{App.PendingUpdate.LatestVersion}");
    }

    private void UpdateDbStats()
    {
        var docs = _vm.DocumentCount;
        var pages = _vm.PageCount;
        var last = _vm.LastSyncTime;
        var lastStr = last.HasValue ? last.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "未同步";
        DbStats.Text = $"文档数：{docs}，页面数：{pages}，最后同步：{lastStr}";
    }

    // ── API Key ──────────────────────────────────────────────────

    private void ApiKeyBox_Changed(object s, RoutedEventArgs e) =>
        _vm.FigmaApiKey = ApiKeyBox.Password;

    private static readonly string HelpUrl = "https://docs.popo.netease.com/team/pc/5_63c60p/pageDetail/82a0a23187c74981ba8461b780f78ac9";

    private void ApiKeyHelp_Click(object s, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(HelpUrl) { UseShellExecute = true }); }
        catch { }
    }

    private void TeamIdHelp_Click(object s, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(HelpUrl) { UseShellExecute = true }); }
        catch { }
    }

    private async void ValidateApiKey_Click(object s, RoutedEventArgs e)
    {
        var key = ApiKeyBox.Password;
        if (string.IsNullOrEmpty(key)) return;
        ApiKeyStatus.Visibility = Visibility.Visible;
        ApiKeyStatus.Text = "验证中...";
        ApiKeyStatus.Foreground = (Brush)FindResource("FgSecondary");
        var ok = await App.Api.ValidateTokenAsync(key);
        ApiKeyStatus.Text = ok ? "✔ API Key 有效" : "✖ API Key 无效或已过期";
        ApiKeyStatus.Foreground = ok
            ? (Brush)FindResource("SuccessGreen")
            : (Brush)FindResource("ErrorRed");
    }

    // ── Teams ────────────────────────────────────────────────────

    private void AddTeam_Click(object s, RoutedEventArgs e)
    {
        var rawId = NewTeamId.Text.Trim();
        var name  = NewTeamName.Text.Trim();
        if (string.IsNullOrEmpty(rawId) || string.IsNullOrEmpty(name)) return;

        var error = TeamConfig.ValidateTeamId(rawId, out var cleanedId);
        if (error != null)
        {
            TeamIdError.Text = error;
            TeamIdError.Visibility = Visibility.Visible;
            return;
        }

        TeamIdError.Visibility = Visibility.Collapsed;
        _vm.AddTeam(cleanedId, name);
        NewTeamId.Text = "";
        NewTeamName.Text = "";
    }

    private void NewTeam_TextChanged(object s, TextChangedEventArgs e)
    {
        if (AddTeamBtn == null) return;
        AddTeamBtn.IsEnabled = !string.IsNullOrWhiteSpace(NewTeamId.Text)
                             && !string.IsNullOrWhiteSpace(NewTeamName.Text);
        // Clear previous validation error when user edits
        if (TeamIdError != null) TeamIdError.Visibility = Visibility.Collapsed;
    }

    private void RemoveTeam_Click(object s, RoutedEventArgs e)
    {
        if (TeamsGrid.SelectedItem is not TeamConfig t) return;

        var result = MessageBox.Show(
            $"确定要删除 Team「{t.DisplayName}」吗？\n\n该操作会同步删除该 Team 下所有已拉取的文档和页面数据。",
            "删除确认",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
            _vm.RemoveTeam(t);
    }

    private void MoveTeamUp_Click(object s, RoutedEventArgs e)
    {
        if (TeamsGrid.SelectedItem is not TeamConfig t) return;
        var idx = _vm.Teams.IndexOf(t);
        if (idx <= 0) return;
        _vm.Teams.Move(idx, idx - 1);
        TeamsGrid.SelectedItem = t;
    }

    private void MoveTeamDown_Click(object s, RoutedEventArgs e)
    {
        if (TeamsGrid.SelectedItem is not TeamConfig t) return;
        var idx = _vm.Teams.IndexOf(t);
        if (idx < 0 || idx >= _vm.Teams.Count - 1) return;
        _vm.Teams.Move(idx, idx + 1);
        TeamsGrid.SelectedItem = t;
    }

    // ── Interval / Startup ───────────────────────────────────────

    private void IntervalSlider_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_vm == null) return;
        var v = (int)e.NewValue;
        _vm.UpdateIntervalHours = v;
        if (IntervalLabel != null) IntervalLabel.Text = $"{v} 小时";
    }

    private void StartupCheck_Changed(object s, RoutedEventArgs e) =>
        _vm.LaunchAtStartup = StartupCheck.IsChecked == true;

    private void OpenInBrowserCheck_Changed(object s, RoutedEventArgs e) =>
        _vm.OpenInBrowser = OpenInBrowserCheck.IsChecked == true;

    private void ThemeCombo_Changed(object s, SelectionChangedEventArgs e)
    {
        if (ThemeCombo.SelectedItem is not System.Windows.Controls.ComboBoxItem item) return;
        var theme = item.Tag as string ?? "Dark";
        _vm.Theme = theme;
        App.ApplyTheme(theme, persist: false); // persist on Save
    }

    // ── Hotkey recorder ──────────────────────────────────────────

    private void HotkeyBox_GotFocus(object s, RoutedEventArgs e)
    {
        _isRecordingHotkey = true;
        HotkeyBox.Text = "请按下快捷键…";
        HotkeyBox.BorderBrush = (Brush)FindResource("AccentBlue");
        HotkeyHint.Foreground = (Brush)FindResource("AccentBlue");
    }

    private void HotkeyBox_LostFocus(object s, RoutedEventArgs e)
    {
        _isRecordingHotkey = false;
        HotkeyBox.BorderBrush = (Brush)FindResource("BorderColor");
        HotkeyHint.Foreground = (Brush)FindResource("FgDisabled");
        // If still showing placeholder, restore current config
        if (HotkeyBox.Text == "请按下快捷键…")
            HotkeyBox.Text = FormatConfigForDisplay(_vm.HotkeyConfig);
    }

    private void HotkeyBox_PreviewKeyDown(object s, KeyEventArgs e)
    {
        if (!_isRecordingHotkey) return;
        e.Handled = true; // prevent text input

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Ignore bare modifier keys — wait for a non-modifier key
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        var modifiers = Keyboard.Modifiers;

        // Must have at least one modifier (Ctrl/Alt/Shift)
        if (modifiers == ModifierKeys.None)
        {
            HotkeyHint.Text = "需要至少一个修饰键（Ctrl / Alt / Shift）";
            HotkeyHint.Foreground = (Brush)FindResource("ErrorRed");
            return;
        }

        var config = HotkeyService.FormatHotkey(key, modifiers);
        // Verify it can be parsed back
        if (HotkeyService.ParseHotkey(config) == null)
        {
            HotkeyHint.Text = "不支持的按键组合，请重试";
            HotkeyHint.Foreground = (Brush)FindResource("ErrorRed");
            return;
        }

        _vm.HotkeyConfig = config;
        HotkeyBox.Text = FormatConfigForDisplay(config);
        HotkeyHint.Text = "点击输入框后按下快捷键组合（需含 Ctrl/Alt/Shift 修饰键）";
        HotkeyHint.Foreground = (Brush)FindResource("FgDisabled");
        _isRecordingHotkey = false;

        // Move focus away so the box shows the result
        Keyboard.ClearFocus();
        FocusManager.SetFocusedElement(this, null);
    }

    private void HotkeyReset_Click(object s, RoutedEventArgs e)
    {
        _vm.HotkeyConfig = "DoubleAlt";
        HotkeyBox.Text = FormatConfigForDisplay("DoubleAlt");
        _isRecordingHotkey = false;
    }

    private static string FormatConfigForDisplay(string config) =>
        string.Equals(config, "DoubleAlt", StringComparison.OrdinalIgnoreCase)
            ? "双击左 Alt"
            : config.Replace("+", " + ");

    // ── Update check ─────────────────────────────────────────────

    private async void CheckUpdate_Click(object s, RoutedEventArgs e)
    {
        CheckUpdateBtn.IsEnabled = false;
        UpdateStatus.Text = "正在检查…";
        UpdateStatus.Foreground = (Brush)FindResource("FgSecondary");
        try
        {
            var info = await App.Updater.CheckForUpdateAsync();
            if (info == null)
            {
                UpdateStatus.Text = "检查失败，请稍后重试";
                UpdateStatus.Foreground = (Brush)FindResource("ErrorRed");
            }
            else if (info.IsNewer)
            {
                App.PendingUpdate = info;
                App.SearchWin.ShowUpdateBadge(info);
                ShowInstallButton($"发现新版本 v{info.LatestVersion}");
            }
            else
            {
                UpdateStatus.Text = $"已是最新版本（v{info.LatestVersion}）";
                UpdateStatus.Foreground = (Brush)FindResource("FgSecondary");
            }
        }
        catch
        {
            UpdateStatus.Text = "检查失败，请检查网络连接";
            UpdateStatus.Foreground = (Brush)FindResource("ErrorRed");
        }
        finally
        {
            CheckUpdateBtn.IsEnabled = true;
        }
    }

    private void ShowInstallButton(string statusText)
    {
        CheckUpdateBtn.Visibility = Visibility.Collapsed;
        InstallUpdateBtn.Visibility = Visibility.Visible;
        UpdateStatus.Text = statusText;
        UpdateStatus.Foreground = (Brush)FindResource("SuccessGreen");
    }

    private async void InstallUpdate_Click(object s, RoutedEventArgs e)
    {
        if (App.PendingUpdate == null) return;
        InstallUpdateBtn.IsEnabled = false;
        UpdateStatus.Text = "正在下载安装包…";
        UpdateStatus.Foreground = (Brush)FindResource("FgSecondary");
        try
        {
            var prog = new Progress<int>(percent =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    UpdateStatus.Text = percent < 100 ? $"下载中 {percent}%…" : "下载完成，正在启动安装…";
                });
            });
            var path = await App.Updater.DownloadInstallerAsync(App.PendingUpdate.DownloadUrl, prog);
            UpdateStatus.Text = "正在启动安装…";
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            Application.Current.Shutdown();
        }
        catch
        {
            InstallUpdateBtn.IsEnabled = true;
            UpdateStatus.Text = "下载失败，请重试";
            UpdateStatus.Foreground = (Brush)FindResource("ErrorRed");
        }
    }

    // ── Sync ─────────────────────────────────────────────────────

    private void ManualSync_Click(object s, RoutedEventArgs e)
    {
        if (App.AutoSync == null) return;
        if (App.AutoSync.IsSyncing)
        {
            SyncStatus.Text = "同步正在进行中…";
            return;
        }

        var teams = App.DB.GetTeams();
        if (teams.Count == 0)
        {
            SyncStatus.Text = "请先添加 Team ID";
            return;
        }

        _vm.Save();
        App.AutoSync.RunNow(force: true);
        SyncStatus.Text = "已在后台开始同步，进度可在搜索框中查看";
    }

    // ── Save / Cancel ────────────────────────────────────────────

    private void Save_Click(object s, RoutedEventArgs e)
    {
        // Snapshot old teams before saving to detect changes
        var oldTeamIds = App.DB.GetTeams().Select(t => t.TeamId).ToHashSet();
        var newTeamIds = _vm.Teams.Select(t => t.TeamId).ToHashSet();

        // Capture BrainMaker fields before save
        _vm.BmAuthAccount = BmAuthAccountBox.Text.Trim();
        _vm.BmAuthKey = BmAuthKeyBox.Password;
        _vm.BmUserCorp = BmUserCorpBox.Text.Trim();
        _vm.BmProject = BmProjectBox.Text.Trim();
        _vm.BmDocset = BmDocsetBox.Text.Trim();

        _vm.Save();

        // Apply new hotkey immediately
        App.Hotkey?.ApplyConfig(_vm.HotkeyConfig);

        // Reconfigure BrainMaker service with new credentials
        App.BrainMaker?.Configure(_vm.BmAuthAccount, _vm.BmAuthKey, _vm.BmUserCorp, _vm.BmProject, _vm.BmDocset);

        // Only trigger sync when teams were ADDED (new data to fetch).
        // Removing teams just deletes local data (handled by SaveTeams) — no sync needed.
        bool teamsAdded = newTeamIds.Except(oldTeamIds).Any();

        if (teamsAdded)
        {
            App.AutoSync?.RunNow(force: true);
            MessageBox.Show(
                "检测到新增 Team，已在后台开始同步文档数据。\n\n同步进度可在搜索框底部查看。",
                "文档同步",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        Close();
    }

    private void Cancel_Click(object s, RoutedEventArgs e) => Close();

    // ── Contact ──────────────────────────────────────────────────

    private void ContactLink_RequestNavigate(object s, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch { }
        e.Handled = true;
    }
}
