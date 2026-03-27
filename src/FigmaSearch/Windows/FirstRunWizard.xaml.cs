using FigmaSearch.Models;
using FigmaSearch.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FigmaSearch.Windows;

public partial class FirstRunWizard : Window
{
    private readonly ObservableCollection<TeamConfig> _teams = new();

    public FirstRunWizard()
    {
        InitializeComponent();
        TeamsItemsControl.ItemsSource = _teams;

        // Start with one empty row so users see the input fields immediately
        _teams.Add(new TeamConfig { SortOrder = 0 });

        TeamsItemsControl.Loaded += (_, _) =>
        {
            FillTeamApiKeyBoxes();
            _teams.CollectionChanged += (_, _) =>
                Dispatcher.BeginInvoke(new Action(FillTeamApiKeyBoxes),
                    System.Windows.Threading.DispatcherPriority.Loaded);
        };
    }

    // ── PasswordBox fill (no data-binding support) ────────────────

    private void FillTeamApiKeyBoxes()
    {
        for (int i = 0; i < _teams.Count; i++)
        {
            var item = TeamsItemsControl.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
            if (item == null) continue;
            var pb = FindChild<PasswordBox>(item);
            if (pb != null && pb.Password != _teams[i].ApiKey)
                pb.Password = _teams[i].ApiKey;
        }
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var result = FindChild<T>(child);
            if (result != null) return result;
        }
        return null;
    }

    // ── Team row events ───────────────────────────────────────────

    private void AddTeam_Click(object s, RoutedEventArgs e)
    {
        _teams.Add(new TeamConfig { SortOrder = _teams.Count });
        Dispatcher.BeginInvoke(new Action(FillTeamApiKeyBoxes),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void TeamRow_LostFocus(object s, RoutedEventArgs e)
    {
        if (s is not FrameworkElement fe || fe.DataContext is not TeamConfig t) return;
        var rawId = t.TeamId?.Trim() ?? "";
        if (string.IsNullOrEmpty(rawId)) { TeamIdError.Visibility = Visibility.Collapsed; return; }

        var error = TeamConfig.ValidateTeamId(rawId, out var cleanedId);
        if (error != null)
        {
            TeamIdError.Text = error;
            TeamIdError.Visibility = Visibility.Visible;
        }
        else
        {
            if (t.TeamId != cleanedId) t.TeamId = cleanedId;
            TeamIdError.Visibility = Visibility.Collapsed;
        }
    }

    private void TeamApiKey_PasswordChanged(object s, RoutedEventArgs e)
    {
        if (s is PasswordBox pb && pb.Tag is TeamConfig t)
            t.ApiKey = pb.Password;
    }

    private void RemoveTeam_Click(object s, RoutedEventArgs e)
    {
        if (s is not FrameworkElement fe || fe.Tag is not TeamConfig t) return;
        _teams.Remove(t);
    }

    private void MoveTeamUp_Click(object s, RoutedEventArgs e)
    {
        if (s is not FrameworkElement fe || fe.Tag is not TeamConfig t) return;
        var idx = _teams.IndexOf(t);
        if (idx <= 0) return;
        _teams.Move(idx, idx - 1);
    }

    private void MoveTeamDown_Click(object s, RoutedEventArgs e)
    {
        if (s is not FrameworkElement fe || fe.Tag is not TeamConfig t) return;
        var idx = _teams.IndexOf(t);
        if (idx < 0 || idx >= _teams.Count - 1) return;
        _teams.Move(idx, idx + 1);
    }

    // ── Help links ────────────────────────────────────────────────

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

    // ── Navigation ────────────────────────────────────────────────

    // "退出" button — close wizard and let App.OnStartup handle shutdown
    private void Skip_Click(object s, RoutedEventArgs e) => Close();

    private void Start_Click(object s, RoutedEventArgs e)
    {
        var key = ApiKeyBox.Password.Trim();

        // Filter out empty/invalid team rows
        var validTeams = _teams
            .Where(t => !string.IsNullOrWhiteSpace(t.TeamId)
                     && TeamConfig.ValidateTeamId(t.TeamId.Trim(), out _) == null
                     && !string.IsNullOrWhiteSpace(t.DisplayName))
            .ToList();

        var hasKey   = !string.IsNullOrEmpty(key);
        var hasTeams = validTeams.Count > 0;

        // Save config and mark first-run complete
        var settings = App.DB.LoadSettings();
        settings.FigmaApiKey     = key;
        settings.LaunchAtStartup = StartupCheck.IsChecked == true;
        settings.IsFirstRun      = false;
        App.DB.SaveSettings(settings);

        // Re-assign correct SortOrders and save only valid teams
        for (int i = 0; i < validTeams.Count; i++) validTeams[i].SortOrder = i;
        if (hasTeams) App.DB.SaveTeams(validTeams);
        StartupManager.Set(settings.LaunchAtStartup);

        if (!hasKey || !hasTeams)
        {
            var missing = new System.Collections.Generic.List<string>();
            if (!hasKey)   missing.Add("Figma API Key");
            if (!hasTeams) missing.Add("至少一个有效的 Team（需填写 Team ID 和显示名称）");

            var msgBox = new Window
            {
                Title = "提示",
                Width = 440, Height = 210,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.SingleBorderWindow,
                Background = (Brush)FindResource("BgPrimary")
            };
            var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
            panel.Children.Add(new TextBlock
            {
                Text = $"您尚未配置 {string.Join(" 和 ", missing)}，Figma 文档搜索功能暂时无法使用。\n\n请前往「设置」中补全配置后即可正常搜索。",
                Foreground = Brushes.OrangeRed,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 20)
            });
            var okBtn = new Button
            {
                Content = "我知道了",
                Style = (Style)FindResource("FlatButton"),
                Background = (Brush)FindResource("AccentBlue"),
                Foreground = Brushes.White,
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

        Close();
    }
}
