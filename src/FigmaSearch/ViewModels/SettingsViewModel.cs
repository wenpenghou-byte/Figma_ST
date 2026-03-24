using FigmaSearch.Models;
using FigmaSearch.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FigmaSearch.ViewModels;

public class SettingsViewModel : INotifyPropertyChanged
{
    private readonly DatabaseService _db;
    private AppSettings _settings;

    public ObservableCollection<TeamConfig> Teams { get; } = new();

    public string FigmaApiKey
    {
        get => _settings.FigmaApiKey;
        set { _settings.FigmaApiKey = value; OnPropertyChanged(); }
    }

    public int UpdateIntervalHours
    {
        get => _settings.UpdateIntervalHours;
        set { _settings.UpdateIntervalHours = Math.Clamp(value, 1, 168); OnPropertyChanged(); }
    }

    public bool LaunchAtStartup
    {
        get => _settings.LaunchAtStartup;
        set { _settings.LaunchAtStartup = value; OnPropertyChanged(); StartupManager.Set(value); }
    }

    public string HotkeyConfig
    {
        get => _settings.HotkeyConfig;
        set { _settings.HotkeyConfig = value; OnPropertyChanged(); }
    }

    public string Theme
    {
        get => _settings.Theme;
        set { _settings.Theme = value; OnPropertyChanged(); }
    }

    public bool OpenInBrowser
    {
        get => _settings.OpenInBrowser;
        set { _settings.OpenInBrowser = value; OnPropertyChanged(); }
    }

    // ── BrainMaker AI ────────────────────────────────────────
    public string BmAuthAccount
    {
        get => _settings.BmAuthAccount;
        set { _settings.BmAuthAccount = value; OnPropertyChanged(); }
    }
    public string BmAuthKey
    {
        get => _settings.BmAuthKey;
        set { _settings.BmAuthKey = value; OnPropertyChanged(); }
    }
    public string BmUserCorp
    {
        get => _settings.BmUserCorp;
        set { _settings.BmUserCorp = value; OnPropertyChanged(); }
    }
    public string BmProject
    {
        get => _settings.BmProject;
        set { _settings.BmProject = value; OnPropertyChanged(); }
    }
    public string BmDocset
    {
        get => _settings.BmDocset;
        set { _settings.BmDocset = value; OnPropertyChanged(); }
    }

    public SettingsViewModel(DatabaseService db)
    {
        _db = db;
        _settings = db.LoadSettings();
        foreach (var t in db.GetTeams()) Teams.Add(t);
    }

    public void AddTeam(string teamId, string displayName)
    {
        Teams.Add(new TeamConfig
        {
            TeamId      = teamId,
            DisplayName = displayName,
            SortOrder   = Teams.Count
        });
    }

    public void RemoveTeam(TeamConfig t) => Teams.Remove(t);

    public void Save()
    {
        _settings.IsFirstRun = false;
        _db.SaveSettings(_settings);
        _db.SaveTeams(Teams);
    }

    public int DocumentCount => _db.GetDocumentCount();
    public int PageCount     => _db.GetPageCount();
    public DateTime? LastSyncTime => _db.GetLastSyncTime();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
