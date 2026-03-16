using FigmaSearch.Models;
using FigmaSearch.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;

namespace FigmaSearch.Windows;

public partial class FirstRunWizard : Window
{
    private readonly ObservableCollection<TeamConfig> _teams = new();

    public FirstRunWizard()
    {
        InitializeComponent();
        TeamsGrid.ItemsSource = _teams;
    }

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
        _teams.Add(new TeamConfig { TeamId = cleanedId, DisplayName = name, SortOrder = _teams.Count });
        NewTeamId.Text = "";
        NewTeamName.Text = "";
    }

    private void NewTeam_TextChanged(object s, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (AddTeamBtn == null) return;
        AddTeamBtn.IsEnabled = !string.IsNullOrWhiteSpace(NewTeamId.Text)
                             && !string.IsNullOrWhiteSpace(NewTeamName.Text);
        // Clear previous validation error when user edits
        if (TeamIdError != null) TeamIdError.Visibility = Visibility.Collapsed;
    }

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

    // "退出" button — close wizard and let App.OnStartup handle shutdown
    private void Skip_Click(object s, RoutedEventArgs e)
    {
        Close();
    }

    private void Start_Click(object s, RoutedEventArgs e)
    {
        var key = ApiKeyBox.Password.Trim();
        if (string.IsNullOrEmpty(key))
        {
            ApiKeyHint.Text = "请先填入 API Key";
            ApiKeyHint.Foreground = System.Windows.Media.Brushes.OrangeRed;
            return;
        }
        if (_teams.Count == 0)
        {
            SyncStatus.Visibility = Visibility.Visible;
            SyncStatus.Foreground = System.Windows.Media.Brushes.OrangeRed;
            SyncStatus.Text = "请至少添加一个 Team ID，否则无法搜索文档";
            return;
        }

        // Save config and mark first-run complete
        var settings = App.DB.LoadSettings();
        settings.FigmaApiKey     = key;
        settings.LaunchAtStartup = StartupCheck.IsChecked == true;
        settings.IsFirstRun      = false;
        App.DB.SaveSettings(settings);
        App.DB.SaveTeams(_teams);
        StartupManager.Set(settings.LaunchAtStartup);

        MessageBox.Show(
            "配置完成！文档数据正在后台同步中。\n\n双击左 Alt 可唤醒搜索框，已完成同步的文档可立即搜索。",
            "开始使用",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        // Close immediately — sync will happen in background via AutoSyncService
        Close();
    }
}
