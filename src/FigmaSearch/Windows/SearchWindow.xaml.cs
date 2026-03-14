using FigmaSearch.Models;
using FigmaSearch.Services;
using FigmaSearch.ViewModels;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace FigmaSearch.Windows;

public partial class SearchWindow : Window
{
    private readonly SearchViewModel _vm;
    private UpdateInfo? _pendingUpdate;
    private bool _hasBeenShown;
    private string? _syncErrorMessage;

    // ── Global mouse hook for outside-click detection ───────────
    private IntPtr _mouseHookId;
    private NativeMethods.LowLevelMouseProc? _mouseHookProc; // prevent GC

    public SearchWindow()
    {
        InitializeComponent();
        _vm = new SearchViewModel(App.DB);
        DataContext = _vm;
    }

    /// <summary>
    /// Bind to AutoSyncService events for live sync progress in the search window.
    /// Called once from App.OnStartup after both SearchWindow and AutoSync are created.
    /// </summary>
    public void BindSyncService(AutoSyncService sync)
    {
        sync.SyncProgressChanged += p =>
            Dispatcher.BeginInvoke(() => OnSyncProgress(p));
        sync.SyncCompleted += success =>
            Dispatcher.BeginInvoke(() => OnSyncCompleted(success));
        sync.SyncFailed += ex =>
            Dispatcher.BeginInvoke(() => OnSyncError(ex));

        // If sync is already running (unlikely but safe), show the bar
        if (sync.IsSyncing)
            ShowSyncProgress("正在同步文档…（视文档数量，可能需要几分钟到几十分钟，已拉取的文档可搜索）");
    }

    // ── Sync status display ──────────────────────────────────────

    private void OnSyncProgress(SyncProgress p)
    {
        _syncErrorMessage = null; // clear any previous error while syncing
        var detail = string.IsNullOrEmpty(p.Detail) ? "" : $" · {p.Detail}";
        ShowSyncProgress($"正在同步{detail}（视文档数量，可能需要几分钟到几十分钟，已拉取的文档可搜索）");
    }

    private void OnSyncCompleted(bool success)
    {
        if (success)
        {
            _syncErrorMessage = null;
            ShowSyncSuccess($"同步完成，共 {App.DB.GetDocumentCount()} 个文档");
            // Auto-hide after 5 seconds
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timer.Tick += (_, _) => { timer.Stop(); HideSyncBar(); };
            timer.Start();
        }
        // If not success but no error message set, it was cancelled — just hide
        else if (_syncErrorMessage == null)
        {
            HideSyncBar();
        }
    }

    private void OnSyncError(Exception ex)
    {
        _syncErrorMessage = ex is FigmaAuthException
            ? "同步失败：API Key 无效或已过期，请在设置中更新"
            : $"同步失败：{ex.Message}";
        ShowSyncError(_syncErrorMessage);
    }

    private void ShowSyncProgress(string text)
    {
        SyncStatusBar.Visibility = Visibility.Visible;
        SyncStatusBar.Background = (Brush)FindResource("SyncProgressBg");
        SyncStatusIcon.Text = "⟳";
        SyncStatusIcon.Foreground = (Brush)FindResource("AccentBlue");
        SyncStatusText.Text = text;
        SyncStatusText.Foreground = (Brush)FindResource("FgSecondary");
    }

    private void ShowSyncSuccess(string text)
    {
        SyncStatusBar.Visibility = Visibility.Visible;
        SyncStatusBar.Background = (Brush)FindResource("SyncSuccessBg");
        SyncStatusIcon.Text = "✓";
        SyncStatusIcon.Foreground = (Brush)FindResource("SuccessGreen");
        SyncStatusText.Text = text;
        SyncStatusText.Foreground = (Brush)FindResource("SuccessGreen");
    }

    private void ShowSyncError(string text)
    {
        SyncStatusBar.Visibility = Visibility.Visible;
        SyncStatusBar.Background = (Brush)FindResource("SyncErrorBg");
        SyncStatusIcon.Text = "✗";
        SyncStatusIcon.Foreground = (Brush)FindResource("ErrorRed");
        SyncStatusText.Text = text;
        SyncStatusText.Foreground = (Brush)FindResource("ErrorTextLight");
    }

    private void HideSyncBar()
    {
        // Only hide if there's no persistent error
        if (_syncErrorMessage != null)
        {
            ShowSyncError(_syncErrorMessage);
            return;
        }
        SyncStatusBar.Visibility = Visibility.Collapsed;
    }

    // ── Existing functionality ───────────────────────────────────

    public void Toggle()
    {
        if (IsVisible) HideWindow(); else ShowWindow();
    }

    public new void Show() => ShowWindow();

    private void ShowWindow()
    {
        _hasBeenShown = true;

        var screen = System.Windows.SystemParameters.WorkArea;
        Left = screen.Left + (screen.Width - Width) / 2;
        Top  = screen.Top  + screen.Height * 0.22;
        base.Show();

        // Activate and focus
        Activate();
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);

        // Install global mouse hook to detect clicks outside the window
        InstallMouseHook();

        // Restore persistent error if any
        if (_syncErrorMessage != null)
            ShowSyncError(_syncErrorMessage);
        // Or show progress if sync is running
        else if (App.AutoSync?.IsSyncing == true)
            ShowSyncProgress("正在同步文档…（视文档数量，可能需要几分钟到几十分钟，已拉取的文档可搜索）");
    }

    private void HideWindow()
    {
        UninstallMouseHook();
        Hide();
        SearchBox.Text = "";
        _vm.ClearSearch();
        RebuildResults();
    }

    // ── Global mouse hook ────────────────────────────────────────

    private void InstallMouseHook()
    {
        if (_mouseHookId != IntPtr.Zero) return; // already installed
        _mouseHookProc = MouseHookCallback;
        using var proc = System.Diagnostics.Process.GetCurrentProcess();
        using var mod = proc.MainModule!;
        _mouseHookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL, _mouseHookProc,
            NativeMethods.GetModuleHandle(mod.ModuleName), 0);
    }

    private void UninstallMouseHook()
    {
        if (_mouseHookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHookId);
            _mouseHookId = IntPtr.Zero;
        }
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsVisible)
        {
            int msg = (int)wParam;
            // React to any mouse button down
            if (msg == NativeMethods.WM_LBUTTONDOWN ||
                msg == NativeMethods.WM_RBUTTONDOWN ||
                msg == NativeMethods.WM_MBUTTONDOWN)
            {
                // Get click position
                var hookStruct = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                var clickPoint = new System.Windows.Point(hookStruct.pt.x, hookStruct.pt.y);

                // Check if click is outside our window bounds
                var windowRect = new System.Windows.Rect(Left, Top, ActualWidth, ActualHeight);
                if (!windowRect.Contains(clickPoint))
                {
                    // Dispatch hide on UI thread (we're in the hook callback)
                    Dispatcher.BeginInvoke(new Action(HideWindow));
                }
            }
        }
        return NativeMethods.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    private void SearchBox_TextChanged(object s, TextChangedEventArgs e)
    {
        _vm.Query = SearchBox.Text;
        RebuildResults();
    }

    private void RebuildResults()
    {
        ResultsPanel.Children.Clear();
        ResultsArea.Visibility = _vm.Results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ResultsScroll.ScrollToTop();
        foreach (var group in _vm.Results)
        {
            var lbl = new TextBlock
            {
                Text       = group.TeamDisplayName.ToUpperInvariant(),
                Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0x96, 0x40)),
                FontSize   = 10, FontWeight = FontWeights.SemiBold,
                Margin     = new Thickness(16, 8, 0, 2)
            };
            ResultsPanel.Children.Add(lbl);
            foreach (var item in group.Items)
                ResultsPanel.Children.Add(BuildDocRow(item));
        }
    }

    private UIElement BuildDocRow(SearchResultItem item)
    {
        var outer = new StackPanel();
        var docGrid = new Grid { Cursor = Cursors.Hand, Background = Brushes.Transparent };
        docGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        docGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var docText = new Controls.HighlightText
        {
            TextValue  = item.DocumentName,
            Keyword    = _vm.Query,
            Foreground = (Brush)FindResource("FgPrimary"),
            FontWeight = FontWeights.Normal,
            Margin     = new Thickness(16, 0, 8, 0),
            Padding    = new Thickness(0, 9, 0, 9),
            FontSize   = 14
        };
        Grid.SetColumn(docText, 0);
        docGrid.Children.Add(docText);

        var pagesPanel = new StackPanel();
        pagesPanel.Visibility = item.IsExpanded ? Visibility.Visible : Visibility.Collapsed;

        if (item.HasMatchedChildren)
        {
            var foldBtn = new Button
            {
                Content = item.IsExpanded ? "▼" : "▶",
                Style   = (Style)FindResource("FlatButton"),
                Margin  = new Thickness(0, 0, 10, 0),
                Tag     = (item, pagesPanel)
            };
            foldBtn.Click += (_, _) =>
            {
                item.IsExpanded = !item.IsExpanded;
                foldBtn.Content = item.IsExpanded ? "▼" : "▶";
                pagesPanel.Visibility = item.IsExpanded ? Visibility.Visible : Visibility.Collapsed;
            };
            Grid.SetColumn(foldBtn, 1);
            docGrid.Children.Add(foldBtn);
        }

        docGrid.MouseLeftButtonUp += (_, _) => { OpenUrl(item.DocumentUrl); HideWindow(); };
        docGrid.MouseEnter += (_, _) => docGrid.Background = (Brush)FindResource("BgHover");
        docGrid.MouseLeave += (_, _) => docGrid.Background = Brushes.Transparent;
        outer.Children.Add(docGrid);

        foreach (var page in item.ChildPages)
        {
            var pageGrid = new Grid { Cursor = Cursors.Hand, Background = Brushes.Transparent };
            var pageText = new Controls.HighlightText
            {
                TextValue  = page.PageName,
                Keyword    = _vm.Query,
                Foreground = (Brush)FindResource("FgSecondary"),
                Margin     = new Thickness(40, 0, 16, 0),
                Padding    = new Thickness(0, 6, 0, 6),
                FontSize   = 13
            };
            pageGrid.Children.Add(pageText);
            pageGrid.MouseLeftButtonUp += (_, _) => { OpenUrl(page.PageUrl); HideWindow(); };
            pageGrid.MouseEnter += (_, _) => pageGrid.Background = (Brush)FindResource("BgHover");
            pageGrid.MouseLeave += (_, _) => pageGrid.Background = Brushes.Transparent;
            pagesPanel.Children.Add(pageGrid);
        }
        outer.Children.Add(pagesPanel);
        return outer;
    }

    /// <summary>
    /// Opens a Figma URL, preferring the desktop client (figma:// protocol).
    /// Falls back to the default browser if the desktop app is not installed
    /// or the protocol launch fails.
    /// </summary>
    private static void OpenUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return;

        // Convert https://www.figma.com/... → figma://...
        var figmaUri = url
            .Replace("https://www.figma.com/", "figma://")
            .Replace("http://www.figma.com/", "figma://");

        try
        {
            // Try desktop client first via figma:// protocol
            Process.Start(new ProcessStartInfo(figmaUri) { UseShellExecute = true });
        }
        catch
        {
            // figma:// not registered or launch failed — fall back to browser
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { }
        }
    }

    public void ShowUpdateBadge(UpdateInfo info)
    {
        _pendingUpdate = info;
        UpdateBtn.Visibility = Visibility.Visible;
    }

    public void ShowApiKeyError() => ApiErrorBar.Visibility = Visibility.Visible;

    private async void UpdateBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate == null) return;
        UpdateBtn.IsEnabled = false;

        // Show progress in the button content
        var originalContent = UpdateBtn.Content;
        UpdateBtn.Content = new TextBlock { Text = "正在下载安装包…", FontSize = 12 };

        try
        {
            var prog = new Progress<int>(percent =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    UpdateBtn.Content = new TextBlock
                    {
                        Text = percent < 100 ? $"下载中 {percent}%…" : "下载完成，正在启动安装…",
                        FontSize = 12
                    };
                });
            });
            var path = await App.Updater.DownloadInstallerAsync(_pendingUpdate.DownloadUrl, prog);
            UpdateBtn.Content = new TextBlock { Text = "正在启动安装…", FontSize = 12 };
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            Application.Current.Shutdown();
        }
        catch
        {
            UpdateBtn.Content = originalContent;
            UpdateBtn.IsEnabled = true;
        }
    }

    private void ApiErrorGotoSettings_Click(object s, RoutedEventArgs e)
    {
        ApiErrorBar.Visibility = Visibility.Collapsed;
        App.OpenSettings();
    }

    private void FoldButton_Click(object s, RoutedEventArgs e) { }

    private void Window_Deactivated(object s, EventArgs e)
    {
        if (!_hasBeenShown) return;
        if (App.IsSettingsOpen) return;
        // Backup: also hide on deactivation (mouse hook is the primary mechanism)
        if (IsVisible) HideWindow();
    }
    private void Window_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) HideWindow();
    }
    private void ResultsScroll_PreviewMouseWheel(object s, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (s is ScrollViewer sv) { sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta / 3.0); e.Handled = true; }
    }

    // ── Win32 P/Invoke ─────────────────────────────────────────────
}

internal static class NativeMethods
{
    public const int WH_MOUSE_LL = 14;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int WM_MBUTTONDOWN = 0x0207;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr GetModuleHandle(string lpModuleName);
}
