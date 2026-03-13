using FigmaSearch.Models;
using FigmaSearch.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;

namespace FigmaSearch.Windows;

public partial class FirstRunWizard : Window
{
    private readonly ObservableCollection<TeamConfig> _teams = new();
    // Set to true only when setup completes successfully (API key + at least one team saved)

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
        try { Process.Start(new ProcessStartInfo("https://icn5csa86jjp.feishu.cn/wiki/YkeawdgdyiyhT1kBdowcy8qfn8a") { UseShellExecute = true }); }
        catch { }
    }

    // "退出" button — exit the app immediately
    private void Skip_Click(object s, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
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
        if (_teams.Count == 0)
        {
            SyncStatus.Visibility = Visibility.Visible;
            SyncStatus.Foreground = System.Windows.Media.Brushes.OrangeRed;
            SyncStatus.Text = "请至少添加一个 Team ID，否则无法搜索文档";
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
