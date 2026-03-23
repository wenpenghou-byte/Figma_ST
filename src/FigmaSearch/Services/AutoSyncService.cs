using FigmaSearch.Models;

namespace FigmaSearch.Services;

/// <summary>
/// Runs background sync every N hours, or on-demand via RunNow().
/// Emits progress so the SearchWindow can show live status.
/// </summary>
public class AutoSyncService : IDisposable
{
    private Timer? _timer;
    private readonly DatabaseService _db;
    private readonly FigmaApiService _api;
    private bool _syncing;
    private CancellationTokenSource? _cts;

    /// <summary>Fires when a sync fails (auth error, network, etc.).</summary>
    public event Action<Exception>? SyncFailed;

    /// <summary>Fires on each progress update during sync (always on a background thread — marshal to UI!).</summary>
    public event Action<SyncProgress>? SyncProgressChanged;

    /// <summary>Fires when sync completes (success or failure). Bool = success.</summary>
    public event Action<bool>? SyncCompleted;

    public bool IsSyncing => _syncing;

    public AutoSyncService(DatabaseService db, FigmaApiService api)
    {
        _db  = db;
        _api = api;
    }

    public void Start(int intervalHours)
    {
        Stop();
        // Check every 15 min whether enough time has passed
        _timer = new Timer(OnTick, null, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15));
    }

    public void Stop() { _timer?.Dispose(); _timer = null; }

    /// <summary>
    /// Trigger an immediate background sync.
    /// - force=true: always sync regardless of last sync time (first run, manual sync)
    /// - force=false: respects the update interval, skips if synced recently (startup check)
    /// </summary>
    public void RunNow(bool force = false)
    {
        if (_syncing) return;
        _ = DoSyncAsync(force);
    }

    private async void OnTick(object? state)
    {
        if (_syncing) return;
        var settings = _db.LoadSettings();
        if (string.IsNullOrEmpty(settings.FigmaApiKey)) return;

        var last = _db.GetLastSyncTime();
        if (last.HasValue && (DateTime.UtcNow - last.Value).TotalHours < settings.UpdateIntervalHours)
            return;

        await DoSyncAsync(force: false);
    }

    private async Task DoSyncAsync(bool force = false)
    {
        if (_syncing) return;

        // When not forced, respect the update interval (same check as OnTick)
        if (!force)
        {
            var s = _db.LoadSettings();
            var last = _db.GetLastSyncTime();
            if (last.HasValue && (DateTime.UtcNow - last.Value).TotalHours < s.UpdateIntervalHours)
                return;
        }

        _syncing = true;
        _cts = new CancellationTokenSource();
        bool success = false;
        try
        {
            var settings = _db.LoadSettings();
            if (string.IsNullOrEmpty(settings.FigmaApiKey)) return;

            var teams = _db.GetTeams();
            if (teams.Count == 0) return;

            var progress = new Progress<SyncProgress>(p => SyncProgressChanged?.Invoke(p));

            await _api.SyncAllTeamsAsync(
                teams,
                settings.FigmaApiKey,
                progress,
                hasPages:          (fileKey) => _db.HasPages(fileKey),
                onFileSynced:      (doc, pages) => _db.UpsertFileData(doc, pages),
                onTeamFinished:    (teamId, currentKeys) =>
                {
                    _db.RemoveDeletedFiles(teamId, currentKeys);
                    _db.SetLastSyncTime(DateTime.UtcNow);
                },
                ct: _cts.Token);
            success = true;
        }
        catch (OperationCanceledException) { /* cancelled, not a failure */ }
        catch (FigmaAuthException ex) { SyncFailed?.Invoke(ex); }
        catch (Exception ex)          { SyncFailed?.Invoke(ex); }
        finally
        {
            _syncing = false;
            _cts?.Dispose();
            _cts = null;
            SyncCompleted?.Invoke(success);
        }
    }

    public void Dispose()
    {
        Stop();
        try { _cts?.Cancel(); } catch { }
    }
}
