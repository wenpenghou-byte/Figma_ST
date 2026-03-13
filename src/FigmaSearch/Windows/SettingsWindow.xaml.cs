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
        UpdateDbStats();
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

    private void ApiKeyHelp_Click(object s, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://icn5csa86jjp.feishu.cn/wiki/YkeawdgdyiyhT1kBdowcy8qfn8a") { UseShellExecute = true }); }
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
        var id = NewTeamId.Text.Trim();
        var name = NewTeamName.Text.Trim();
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name)) return;
        _vm.AddTeam(id, name);
        NewTeamId.Text = "";
        NewTeamName.Text = "";
    }

    private void NewTeam_TextChanged(object s, TextChangedEventArgs e)
    {
        if (AddTeamBtn == null) return;
        AddTeamBtn.IsEnabled = !string.IsNullOrWhiteSpace(NewTeamId.Text)
                             && !string.IsNullOrWhiteSpace(NewTeamName.Text);
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
        App.AutoSync.RunNow();
        SyncStatus.Text = "已在后台开始同步，进度可在搜索框中查看";
    }

    // ── Save / Cancel ────────────────────────────────────────────

    private void Save_Click(object s, RoutedEventArgs e)
    {
        _vm.Save();
        // Apply new hotkey immediately
        App.Hotkey?.ApplyConfig(_vm.HotkeyConfig);
        // Trigger background sync so new/changed teams are fetched immediately
        App.AutoSync?.RunNow();
        Close();
    }

    private void Cancel_Click(object s, RoutedEventArgs e) => Close();
}
