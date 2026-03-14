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
    /// <summary>
    /// Briefly suppress Deactivated after ShowWindow() to avoid the DoubleAlt
    /// key-up event causing an immediate spurious deactivation.
    /// </summary>
    private DateTime _suppressDeactivateUntil = DateTime.MinValue;
    /// <summary>Whether the AI response area is currently shown (replaces search results).</summary>
    private bool _isAiMode;
    private CancellationTokenSource? _aiCts;

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
        // Suppress Deactivated briefly — on DoubleAlt the pending Alt-up event
        // causes Windows to steal focus right after we show, triggering a
        // spurious Deactivated before ForceActivateAndFocus completes.
        _suppressDeactivateUntil = DateTime.UtcNow.AddMilliseconds(200);

        var screen = System.Windows.SystemParameters.WorkArea;
        Left = screen.Left + (screen.Width - Width) / 2;
        Top  = screen.Top  + screen.Height * 0.22;
        base.Show();

        // Force Windows-level activation so keyboard input goes to this window,
        // not the desktop. Required for overlay windows (WindowStyle=None,
        // ShowInTaskbar=False) activated from a global hotkey hook.
        ForceActivateAndFocus();

        // Restore persistent error if any
        if (_syncErrorMessage != null)
            ShowSyncError(_syncErrorMessage);
        // Or show progress if sync is running
        else if (App.AutoSync?.IsSyncing == true)
            ShowSyncProgress("正在同步文档…（视文档数量，可能需要几分钟到几十分钟，已拉取的文档可搜索）");
    }

    /// <summary>
    /// Uses Win32 AttachThreadInput + SetForegroundWindow to reliably steal focus
    /// from whatever window currently owns it, then sets WPF keyboard focus to
    /// the search TextBox via a deferred Dispatcher callback.
    /// </summary>
    private void ForceActivateAndFocus()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();

        // Attach our thread to the foreground window's thread so Windows allows
        // us to call SetForegroundWindow (otherwise it silently fails).
        var foregroundHwnd = GetForegroundWindow();
        var foregroundThread = GetWindowThreadProcessId(foregroundHwnd, IntPtr.Zero);
        var ourThread = GetWindowThreadProcessId(hwnd, IntPtr.Zero);

        bool attached = false;
        if (foregroundThread != ourThread)
        {
            attached = AttachThreadInput(foregroundThread, ourThread, true);
        }

        try
        {
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
                AttachThreadInput(foregroundThread, ourThread, false);
        }

        Activate();

        // Defer keyboard focus until after WPF finishes layout/render.
        // Guard: only focus if window is still visible (user may have dismissed it
        // between Show() and this callback via Deactivated or Escape).
        Dispatcher.InvokeAsync(() =>
        {
            if (!IsVisible) return;
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
        }, DispatcherPriority.Input);
    }

    private void HideWindow()
    {
        Hide();
        ExitAiMode();
        SearchBox.Text = "";
        _vm.ClearSearch();
        RebuildResults();
    }

    private void SearchBox_TextChanged(object s, TextChangedEventArgs e)
    {
        // Any text change exits AI mode and returns to normal search
        if (_isAiMode) ExitAiMode();
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
                Foreground = (Brush)FindResource("FgTeamLabel"),
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

        docGrid.MouseLeftButtonUp += (_, _) => OpenUrl(item.DocumentUrl);
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
            pageGrid.MouseLeftButtonUp += (_, _) => OpenUrl(page.PageUrl);
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

    // ── AI Ask ───────────────────────────────────────────────────

    private async void AskAi_Click(object s, RoutedEventArgs e)
    {
        var question = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(question)) return;

        if (!App.BrainMaker.IsConfigured)
        {
            EnterAiMode();
            AiStatusLabel.Visibility = Visibility.Collapsed;
            AiResponseText.Text = "AI 功能未配置。请在「设置 → AI 问答」中填写 Auth Account、Auth Key 和 User Corp。";
            return;
        }

        // Cancel any previous request
        _aiCts?.Cancel();
        _aiCts = new CancellationTokenSource();
        var ct = _aiCts.Token;

        EnterAiMode();
        AiStatusLabel.Visibility = Visibility.Visible;
        AiStatusLabel.Text = "正在思考…";
        AiResponseText.Text = "";
        AskAiBtn.IsEnabled = false;

        try
        {
            var response = await App.BrainMaker.ChatAsync(question, ct);
            if (ct.IsCancellationRequested) return;
            AiStatusLabel.Visibility = Visibility.Collapsed;
            AiResponseText.Text = response;
        }
        catch (OperationCanceledException)
        {
            // User cancelled (typed new text, pressed Esc, etc.) — do nothing
        }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested) return;
            AiStatusLabel.Visibility = Visibility.Collapsed;
            AiResponseText.Text = $"请求失败：{ex.Message}";
        }
        finally
        {
            AskAiBtn.IsEnabled = true;
        }
    }

    private void EnterAiMode()
    {
        _isAiMode = true;
        ResultsArea.Visibility = Visibility.Collapsed;
        AiResultArea.Visibility = Visibility.Visible;
    }

    private void ExitAiMode()
    {
        _isAiMode = false;
        _aiCts?.Cancel();
        AiResultArea.Visibility = Visibility.Collapsed;
        AiStatusLabel.Visibility = Visibility.Collapsed;
        AiResponseText.Text = "";
    }

    private void Window_Deactivated(object s, EventArgs e)
    {
        if (!_hasBeenShown) return;
        if (App.IsSettingsOpen) return;
        // After ShowWindow() we suppress Deactivated for a short window because
        // DoubleAlt's pending Alt key-up causes Windows to immediately steal
        // focus, which would hide the window before the user sees it.
        if (DateTime.UtcNow < _suppressDeactivateUntil)
        {
            // Re-activate instead of hiding — the user just opened the window.
            ForceActivateAndFocus();
            return;
        }
        HideWindow();
    }
    private void Window_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) HideWindow();
    }
    private void ResultsScroll_PreviewMouseWheel(object s, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (s is ScrollViewer sv) { sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta / 3.0); e.Handled = true; }
    }

    // ── Win32 P/Invoke for reliable window activation ────────────

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
