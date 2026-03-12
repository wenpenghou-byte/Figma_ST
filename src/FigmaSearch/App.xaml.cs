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

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single instance check
        var mutex = new System.Threading.Mutex(true, "FigmaSearch_SingleInstance", out bool isNew);
        if (!isNew) { Shutdown(); return; }

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
        }

        // Build tray icon
        _trayIcon = BuildTrayIcon();

        // Search window (hidden initially)
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
            ToolTipText = "FigmaSearch",
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

    public static void OpenSettings()
    {
        var win = new SettingsWindow();
        win.ShowDialog();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        Hotkey?.Dispose();
        AutoSync?.Dispose();
        DB?.Dispose();
        base.OnExit(e);
    }
}
