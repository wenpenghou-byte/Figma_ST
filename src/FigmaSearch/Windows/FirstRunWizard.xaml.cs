using FigmaSearch.Models;
using FigmaSearch.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

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

    private void Skip_Click(object s, RoutedEventArgs e)
    {
        var settings = App.DB.LoadSettings();
        settings.IsFirstRun = false;
        App.DB.SaveSettings(settings);
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
        var settings = App.DB.LoadSettings();
        settings.FigmaApiKey = key;
        settings.IsFirstRun  = false;
        settings.LaunchAtStartup = StartupCheck.IsChecked == true;
        App.DB.SaveSettings(settings);
        App.DB.SaveTeams(_teams);
        StartupManager.Set(settings.LaunchAtStartup);
        Close();
    }
}
