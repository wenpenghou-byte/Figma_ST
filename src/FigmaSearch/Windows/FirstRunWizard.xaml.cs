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
        var id = NewTeamId.Text.Trim();
        var name = NewTeamName.Text.Trim();
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name)) return;
        _teams.Add(new TeamConfig { TeamId = id, DisplayName = name, SortOrder = _teams.Count });
        NewTeamId.Text = "";
        NewTeamName.Text = "";
    }

    private void ApiKeyHelp_Click(object s, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://docs.popo.netease.com/team/pc/5_63c60p/pageDetail/82a0a23187c74981ba8461b78ac9") { UseShellExecute = true }); }
        catch { }
    }

    private void Skip_Click(object s, RoutedEventArgs e)
    {
        var settings = App.DB.LoadSettings();
        settings.IsFirstRun = false;
        App.DB.SaveSettings(settings);
        Close();
    }

    private async void Start_Click(object s, RoutedEventArgs e)
    {
        var key = ApiKeyBox.Password.Trim();
        if (string.IsNullOrEmpty(key))
        {
            ApiKeyHint.Text = "请先填入 API Key";
            ApiKeyHint.Foreground = System.Windows.Media.Brushes.OrangeRed;
            return;
        }

        // Save settings
        var settings = App.DB.LoadSettings();
        settings.FigmaApiKey = key;
        settings.IsFirstRun  = false;
        settings.LaunchAtStartup = StartupCheck.IsChecked == true;
        App.DB.SaveSettings(settings);
        App.DB.SaveTeams(_teams);
        StartupManager.Set(settings.LaunchAtStartup);

        if (_teams.Count == 0)
        {
            SyncStatus.Visibility = Visibility.Visible;
            SyncStatus.Text = "未添加 Team，跳过同步。可在设置中添加。";
            await System.Threading.Tasks.Task.Delay(1200);
            Close();
            return;
        }

        // Show syncing state
        StartButton.IsEnabled = false;
        SkipButton.IsEnabled  = false;
        SyncStatus.Visibility = Visibility.Visible;
        SyncStatus.Foreground = (System.Windows.Media.Brush)FindResource("AccentBlue");
        SyncStatus.Text = "正在连接 Figma，拉取数据...";

        int totalFiles = 0;
        var progress = new Progress<SyncProgress>(p =>
        {
            SyncStatus.Text = $"{p.Phase}：{p.Detail}";
        });

        try
        {
            await App.Api.SyncAllTeamsAsync(
                _teams.ToList(),
                key,
                progress,
                onTeamSynced: (teamId, files, pages) =>
                {
                    App.DB.ReplaceTeamData(teamId, files, pages);
                    totalFiles += files.Count;
                });

            SyncStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
            SyncStatus.Text = $"同步完成，共 {totalFiles} 个文件！";
            await System.Threading.Tasks.Task.Delay(1200);
            Close();
        }
        catch (Exception ex)
        {
            SyncStatus.Foreground = System.Windows.Media.Brushes.OrangeRed;
            SyncStatus.Text = $"同步失败：{ex.Message}";
            StartButton.IsEnabled = true;
            SkipButton.IsEnabled  = true;
        }
    }
}
