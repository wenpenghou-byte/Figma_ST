using FigmaSearch.Models;
using FigmaSearch.Services;
using FigmaSearch.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FigmaSearch.Windows;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

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

    private void ApiKeyBox_Changed(object s, RoutedEventArgs e) =>
        _vm.FigmaApiKey = ApiKeyBox.Password;

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

    private void AddTeam_Click(object s, RoutedEventArgs e)
    {
        var id = NewTeamId.Text.Trim();
        var name = NewTeamName.Text.Trim();
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name)) return;
        _vm.AddTeam(id, name);
        NewTeamId.Text = "";
        NewTeamName.Text = "";
    }

    private void RemoveTeam_Click(object s, RoutedEventArgs e)
    {
        if (TeamsGrid.SelectedItem is TeamConfig t) _vm.RemoveTeam(t);
    }

    private void IntervalSlider_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        var v = (int)e.NewValue;
        _vm.UpdateIntervalHours = v;
        if (IntervalLabel != null) IntervalLabel.Text = $"{v} 小时";
    }

    private void StartupCheck_Changed(object s, RoutedEventArgs e) =>
        _vm.LaunchAtStartup = StartupCheck.IsChecked == true;

    private async void ManualSync_Click(object s, RoutedEventArgs e)
    {
        SyncProgressBar.Visibility = Visibility.Visible;
        SyncStatus.Text = "同步中...";
        try
        {
            var teams = App.DB.GetTeams();
            if (teams.Count == 0)
            {
                SyncStatus.Text = "请先添加 Team ID";
                return;
            }

            // Dispatch progress updates back to UI thread
            var progress = new Progress<FigmaSearch.Models.SyncProgress>(p =>
            {
                Dispatcher.Invoke(() =>
                {
                    SyncDetailText.Text = $"[{p.TeamName}] {p.Phase}: {p.Detail}";
                });
            });

            // onTeamSynced callback persists each team as it completes
            await App.Api.SyncAllTeamsAsync(
                teams,
                _vm.FigmaApiKey,
                progress,
                onTeamSynced: (teamId, docs, pages) =>
                    App.DB.ReplaceTeamData(teamId, docs, pages));

            SyncStatus.Text = $"同步完成！共 {App.DB.GetDocumentCount()} 个文档";
            UpdateDbStats();
        }
        catch (FigmaAuthException ex)
        {
            SyncStatus.Text = $"错误：{ex.Message}";
            ApiKeyBox.BorderBrush = (Brush)FindResource("ErrorRed");
        }
        catch (Exception ex)
        {
            SyncStatus.Text = $"同步失败：{ex.Message}";
        }
        finally { SyncProgressBar.Visibility = Visibility.Collapsed; }
    }

    private void Save_Click(object s, RoutedEventArgs e) { _vm.Save(); Close(); }
    private void Cancel_Click(object s, RoutedEventArgs e) => Close();
}
