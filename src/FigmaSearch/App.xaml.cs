using FigmaSearch.Services;
using FigmaSearch.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using System.Windows;

namespace FigmaSearch;

public partial class App : Application
{
    public static DatabaseService DB { get; private set; } = null!;
    public static FigmaApiService Api { get; private set; } = null!;
    public static UpdateService   Updater { get; private set; } = null!;
    public static AutoSyncService AutoSync { get; private set; } = null!;
    public static HotkeyService   Hotkey { get; private set; } = null!;
    public static SearchWindow    SearchWin { get; private set; } = null!;
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

        var settings = DB.LoadSettings();

        // First run: show wizard
        if (settings.IsFirstRun)
        {
            var wizard = new FirstRunWizard();
            wizard.ShowDialog();
            settings = DB.LoadSettings();
            // Exit if user closed wizard without completing setup
            // (no API key OR no teams means nothing was configured at all)
            if (string.IsNullOrEmpty(settings.FigmaApiKey) || DB.GetTeams().Count == 0)
            {
                // Setup incomplete — release mutex so user can relaunch
                _instanceMutex.ReleaseMutex();
                _instanceMutex.Dispose();
                _instanceMutex = null;
                Shutdown();
                return;
            }
            // If IsFirstRun is still true here, sync was interrupted but key+teams are saved.
            // Fall through and start normally — user can trigger sync from Settings.
            if (settings.IsFirstRun)
            {
                settings.IsFirstRun = false;
                DB.SaveSettings(settings);
            }
        }

        // Build tray icon
        _trayIcon = BuildTrayIcon();

        // Search window (hidden initially, created AFTER setup is confirmed complete)
        SearchWin = new SearchWindow();

        // Hotkey
        Hotkey = new HotkeyService(Dispatcher);
        Hotkey.DoubleAltPressed += (_, _) => SearchWin.Toggle();

        // Auto sync
        AutoSync = new AutoSyncService(DB, Api);
        AutoSync.SyncFailed += OnSyncFailed;
        if (!string.IsNullOrEmpty(settings.FigmaApiKey))
            AutoSync.Start(settings.UpdateIntervalHours);

        // Version check (once per day)
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        if (settings.LastUpdateCheckDate != today)
        {
            settings.LastUpdateCheckDate = today;
            DB.SaveSettings(settings);
            _ = CheckForUpdateAsync();
        }
    }

    private async System.Threading.Tasks.Task CheckForUpdateAsync()
    {
        var info = await Updater.CheckForUpdateAsync();
        if (info?.IsNewer == true)
            Dispatcher.Invoke(() => SearchWin.ShowUpdateBadge(info));
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
        var itemShow = new System.Windows.Controls.MenuItem { Header = "显示搜索框" };
        itemShow.Click += (_, _) => SearchWin.Show();
        var itemSettings = new System.Windows.Controls.MenuItem { Header = "设置" };
        itemSettings.Click += (_, _) => OpenSettings();
        var itemExit = new System.Windows.Controls.MenuItem { Header = "退出" };
        itemExit.Click += (_, _) => { Hotkey?.Dispose(); AutoSync?.Dispose(); Shutdown(); };
        menu.Items.Add(itemShow);
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
            // Already open — bring it to front
            if (!_settingsWindow.IsVisible) _settingsWindow.Show();
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow();
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        Hotkey?.Dispose();
        AutoSync?.Dispose();
        DB?.Dispose();
        try { _instanceMutex?.ReleaseMutex(); } catch { }
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
