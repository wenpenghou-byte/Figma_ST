using FigmaSearch.Models;

namespace FigmaSearch.Services;

/// <summary>
/// Runs background sync every N hours. Silent - no progress UI.
/// </summary>
public class AutoSyncService : IDisposable
{
    private Timer? _timer;
    private readonly DatabaseService _db;
    private readonly FigmaApiService _api;
    private bool _syncing;

    public event Action<Exception>? SyncFailed;

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

    private async void OnTick(object? state)
    {
        if (_syncing) return;
        var settings = _db.LoadSettings();
        if (string.IsNullOrEmpty(settings.FigmaApiKey)) return;

        var last = _db.GetLastSyncTime();
        if (last.HasValue && (DateTime.UtcNow - last.Value).TotalHours < settings.UpdateIntervalHours)
            return;

        _syncing = true;
        try
        {
            var teams = _db.GetTeams();
            if (teams.Count == 0) return;

            // Collect already-synced file keys across all teams for resume support
            var alreadySynced = teams
                .SelectMany(t => _db.GetSyncedFileKeys(t.TeamId))
                .ToHashSet();

            var progress = new Progress<SyncProgress>(); // silent, no UI

            await _api.SyncAllTeamsAsync(
                teams,
                settings.FigmaApiKey,
                progress,
                alreadySyncedKeys: alreadySynced,
                onFileSynced:      (doc, pages) => _db.UpsertFileData(doc, pages),
                onTeamFinished:    (teamId, currentKeys) =>
                {
                    _db.RemoveDeletedFiles(teamId, currentKeys);
                    _db.SetLastSyncTime(DateTime.UtcNow);
                });
        }
        catch (FigmaAuthException ex) { SyncFailed?.Invoke(ex); }
        catch (Exception ex)          { SyncFailed?.Invoke(ex); }
        finally { _syncing = false; }
    }

    public void Dispose() => Stop();
}
