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
        var hasKey = !string.IsNullOrEmpty(key);
        var hasTeams = _teams.Count > 0;

        // Save config and mark first-run complete (even if incomplete)
        var settings = App.DB.LoadSettings();
        settings.FigmaApiKey     = key;
        settings.LaunchAtStartup = StartupCheck.IsChecked == true;
        settings.IsFirstRun      = false;
        App.DB.SaveSettings(settings);
        if (hasTeams) App.DB.SaveTeams(_teams);
        StartupManager.Set(settings.LaunchAtStartup);

        if (!hasKey || !hasTeams)
        {
            // Build a specific message about what's missing
            var missing = new System.Collections.Generic.List<string>();
            if (!hasKey) missing.Add("Figma API Key");
            if (!hasTeams) missing.Add("Team ID");

            var msgBox = new Window
            {
                Title = "提示",
                Width = 420, Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.SingleBorderWindow,
                Background = (System.Windows.Media.Brush)FindResource("BgPrimary")
            };
            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(24, 20, 24, 20) };
            panel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = $"您尚未配置 {string.Join(" 和 ", missing)}，Figma 文档搜索功能暂时无法使用。\n\n请前往「设置」中补全配置后即可正常搜索。",
                Foreground = System.Windows.Media.Brushes.OrangeRed,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 20)
            });
            var okBtn = new System.Windows.Controls.Button
            {
                Content = "我知道了",
                Style = (Style)FindResource("FlatButton"),
                Background = (System.Windows.Media.Brush)FindResource("AccentBlue"),
                Foreground = System.Windows.Media.Brushes.White,
                Padding = new Thickness(20, 8, 20, 8),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            okBtn.Click += (_, _) => msgBox.Close();
            panel.Children.Add(okBtn);
            msgBox.Content = panel;
            msgBox.ShowDialog();
        }
        else
        {
            MessageBox.Show(
                "配置完成！文档数据正在后台同步中。\n\n双击左 Alt 可唤醒搜索框，已完成同步的文档可立即搜索。",
                "开始使用",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        // Close immediately — sync will happen in background via AutoSyncService
        Close();
    }
}
