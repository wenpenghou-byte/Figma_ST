using FigmaSearch.Services;
using FigmaSearch.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using System.Windows;

namespace FigmaSearch;

public partial class App : Application
{
    public static DatabaseService    DB { get; private set; } = null!;
    public static FigmaApiService    Api { get; private set; } = null!;
    public static UpdateService      Updater { get; private set; } = null!;
    public static AutoSyncService    AutoSync { get; private set; } = null!;
    public static HotkeyService      Hotkey { get; private set; } = null!;
    public static BrainMakerService  BrainMaker { get; private set; } = null!;
    public static SearchWindow       SearchWin { get; private set; } = null!;

    /// <summary>Global pending update info, shared between SearchWindow and SettingsWindow.</summary>
    public static UpdateInfo? PendingUpdate { get; set; }
    private TaskbarIcon? _trayIcon;
    // Keep mutex alive for the process lifetime to prevent second instance
    private static System.Threading.Mutex? _instanceMutex;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single instance check
        _instanceMutex = new System.Threading.Mutex(true, "FigmaSearch_SingleInstance", out bool isNew);
        if (!isNew)
        {
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }

        DB      = new DatabaseService();
        Api     = new FigmaApiService();
        Updater = new UpdateService();
        BrainMaker = new BrainMakerService();

        var settings = DB.LoadSettings();

        // Apply saved theme before any window is created
        ApplyTheme(settings.Theme, persist: false);

        // Configure BrainMaker AI with saved credentials
        BrainMaker.Configure(settings.BmAuthAccount, settings.BmAuthKey, settings.BmUserCorp, settings.BmProject, settings.BmDocset);

        // First run: show wizard
        if (settings.IsFirstRun)
        {
            var wizard = new FirstRunWizard();
            wizard.ShowDialog();
            settings = DB.LoadSettings();
            // If user closed wizard via "退出" without completing, exit app
            if (settings.IsFirstRun)
            {
                Shutdown();
                return;
            }
        }

        // Build tray icon
        _trayIcon = BuildTrayIcon();

        // Search window (hidden initially)
        SearchWin = new SearchWindow();

        // Hotkey — configurable via settings
        Hotkey = new HotkeyService(Dispatcher);
        Hotkey.HotkeyPressed += (_, _) => SearchWin.Toggle();
        Hotkey.ApplyConfig(settings.HotkeyConfig);

        // Auto sync — SearchWindow subscribes to events for live progress display
        AutoSync = new AutoSyncService(DB, Api);
        AutoSync.SyncFailed += OnSyncFailed;
        SearchWin.BindSyncService(AutoSync);

        if (!string.IsNullOrEmpty(settings.FigmaApiKey))
        {
            AutoSync.Start(settings.UpdateIntervalHours);
            // Startup check: respects interval (force=false), won't sync if recently synced
            AutoSync.RunNow(force: false);
        }

        // Version check (every startup)
        _ = CheckForUpdateAsync();
    }

    private async System.Threading.Tasks.Task CheckForUpdateAsync()
    {
        var info = await Updater.CheckForUpdateAsync();
        if (info?.IsNewer == true)
        {
            PendingUpdate = info;
            Dispatcher.Invoke(() => SearchWin.ShowUpdateBadge(info));
        }
    }

    private void OnSyncFailed(Exception ex)
    {
        if (ex is FigmaAuthException)
            Dispatcher.Invoke(() => SearchWin.ShowApiKeyError());
    }

    private TaskbarIcon BuildTrayIcon()
    {
        var icon = new TaskbarIcon
        {
            ToolTipText = "肥姑妈搜",
        };
        try { icon.IconSource = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Resources/tray.ico")); } catch { }

        var menu = new System.Windows.Controls.ContextMenu();
        var itemSettings = new System.Windows.Controls.MenuItem { Header = "设置" };
        itemSettings.Click += (_, _) => OpenSettings();
        var itemExit = new System.Windows.Controls.MenuItem { Header = "退出" };
        itemExit.Click += (_, _) => Shutdown();
        menu.Items.Add(itemSettings);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(itemExit);
        icon.ContextMenu = menu;
        return icon;
    }

    private static SettingsWindow? _settingsWindow;

    /// <summary>True while the Settings window is instantiated (open or in the process of opening).</summary>
    public static bool IsSettingsOpen => _settingsWindow != null;

    public static void OpenSettings()
    {
        if (_settingsWindow != null)
        {
            if (!_settingsWindow.IsVisible) _settingsWindow.Show();
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow();
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>
    /// Switch application theme at runtime. All DynamicResource bindings update immediately.
    /// </summary>
    /// <param name="theme">"Dark" or "Light"</param>
    /// <param name="persist">Whether to save the preference to the database.</param>
    public static void ApplyTheme(string theme, bool persist = true)
    {
        var file = theme == "Light" ? "LightTheme.xaml" : "DarkTheme.xaml";
        var uri  = new Uri($"Resources/Themes/{file}", UriKind.Relative);
        var dict = new ResourceDictionary { Source = uri };
        Current.Resources.MergedDictionaries[0] = dict;
        if (persist)
        {
            var s = DB.LoadSettings();
            s.Theme = theme;
            DB.SaveSettings(s);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _trayIcon?.Dispose(); } catch { }
        try { Hotkey?.Dispose(); } catch { }
        try { AutoSync?.Dispose(); } catch { }
        try { BrainMaker?.Dispose(); } catch { }
        try { DB?.Dispose(); } catch { }
        try { _instanceMutex?.ReleaseMutex(); } catch { }
        try { _instanceMutex?.Dispose(); } catch { }
        base.OnExit(e);
    }
}
