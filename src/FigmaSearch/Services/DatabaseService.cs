using Dapper;
using FigmaSearch.Models;
using Microsoft.Data.Sqlite;
using System.IO;

namespace FigmaSearch.Services;

public class DatabaseService : IDisposable
{
    private readonly string _dbPath;
    private SqliteConnection? _conn;

    public DatabaseService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FigmaSearch");
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, "figmasearch.db");
        Init();
    }

    private SqliteConnection Conn()
    {
        if (_conn == null || _conn.State != System.Data.ConnectionState.Open)
        {
            _conn = new SqliteConnection($"Data Source={_dbPath}");
            _conn.Open();
        }
        return _conn;
    }
    private void Init()
    {
        Conn().Execute(@"
            CREATE TABLE IF NOT EXISTS Settings (key TEXT PRIMARY KEY, value TEXT);
            CREATE TABLE IF NOT EXISTS Teams (id INTEGER PRIMARY KEY AUTOINCREMENT, team_id TEXT NOT NULL UNIQUE, display_name TEXT NOT NULL, sort_order INTEGER DEFAULT 0);
            CREATE TABLE IF NOT EXISTS Documents (key TEXT PRIMARY KEY, project_id TEXT, project_name TEXT, team_id TEXT NOT NULL, name TEXT NOT NULL, url TEXT NOT NULL, last_synced TEXT);
            CREATE TABLE IF NOT EXISTS Pages (id TEXT PRIMARY KEY, document_key TEXT NOT NULL, name TEXT NOT NULL, url TEXT NOT NULL);
            CREATE VIRTUAL TABLE IF NOT EXISTS SearchIndex USING fts5(entity_type, entity_id, display_name, team_id, document_key);
        ");
    }

    public string? GetSetting(string key) =>
        Conn().QueryFirstOrDefault<string>("SELECT value FROM Settings WHERE key=@key", new { key });

    public void SetSetting(string key, string? value) =>
        Conn().Execute("INSERT INTO Settings(key,value) VALUES(@key,@value) ON CONFLICT(key) DO UPDATE SET value=@value", new { key, value });

    public AppSettings LoadSettings()
    {
        var s = new AppSettings
        {
            FigmaApiKey         = GetSetting("figma_api_key") ?? "",
            UpdateIntervalHours = int.TryParse(GetSetting("update_interval_hours"), out var h) ? h : 48,
            LaunchAtStartup     = GetSetting("launch_at_startup") == "1",
            HotkeyConfig        = GetSetting("hotkey_config") ?? "DoubleAlt",
            LastUpdateCheckDate = GetSetting("last_update_check_date") ?? "",
            AppVersion          = GetSetting("app_version") ?? "1.0.0",
            IsFirstRun          = GetSetting("is_first_run") != "0"
        };
        if (DateTime.TryParse(GetSetting("last_sync_time"), out var dt)) s.LastSyncTime = dt;
        return s;
    }
    public void SaveSettings(AppSettings s)
    {
        SetSetting("figma_api_key",         s.FigmaApiKey);
        SetSetting("update_interval_hours",  s.UpdateIntervalHours.ToString());
        SetSetting("launch_at_startup",      s.LaunchAtStartup ? "1" : "0");
        SetSetting("hotkey_config",          s.HotkeyConfig);
        SetSetting("last_update_check_date", s.LastUpdateCheckDate);
        SetSetting("app_version",            s.AppVersion);
        SetSetting("is_first_run",           s.IsFirstRun ? "1" : "0");
        SetSetting("last_sync_time",         s.LastSyncTime?.ToString("o") ?? "");
    }

    public List<TeamConfig> GetTeams() =>
        Conn().Query<TeamConfig>("SELECT id, team_id TeamId, display_name DisplayName, sort_order SortOrder FROM Teams ORDER BY sort_order, id").ToList();

    public void SaveTeams(IEnumerable<TeamConfig> teams)
    {
        var c = Conn(); using var tx = c.BeginTransaction();

        // Detect removed teams and purge their documents/pages/search index
        var oldTeamIds = c.Query<string>("SELECT team_id FROM Teams", transaction: tx).ToHashSet();
        var newTeamIds = teams.Select(t => t.TeamId).ToHashSet();
        var removedIds = oldTeamIds.Except(newTeamIds).ToList();
        foreach (var teamId in removedIds)
        {
            var fileKeys = c.Query<string>("SELECT key FROM Documents WHERE team_id=@teamId", new { teamId }, tx).ToList();
            if (fileKeys.Count > 0)
            {
                var inList = string.Join(",", fileKeys.Select(k => $"'{k.Replace("'", "''")}'"));
                c.Execute($"DELETE FROM Pages       WHERE document_key IN ({inList})", transaction: tx);
                c.Execute($"DELETE FROM SearchIndex WHERE document_key IN ({inList})", transaction: tx);
            }
            c.Execute("DELETE FROM Documents   WHERE team_id=@teamId", new { teamId }, tx);
            c.Execute("DELETE FROM SearchIndex WHERE team_id=@teamId", new { teamId }, tx);
        }

        c.Execute("DELETE FROM Teams", transaction: tx);
        int order = 0;
        foreach (var t in teams)
            c.Execute("INSERT INTO Teams(team_id,display_name,sort_order) VALUES(@TeamId,@DisplayName,@SortOrder)",
                new { t.TeamId, t.DisplayName, SortOrder = order++ }, transaction: tx);
        tx.Commit();
    }
    // ── Legacy full-replace (kept for compatibility, not used by incremental sync) ──
    public void ReplaceTeamData(string teamId, List<FigmaFile> docs, List<FigmaPage> pages)
    {
        var c = Conn(); using var tx = c.BeginTransaction();
        var oldKeys = c.Query<string>("SELECT key FROM Documents WHERE team_id=@teamId", new { teamId }, tx).ToList();
        if (oldKeys.Count > 0)
        {
            var inList = string.Join(",", oldKeys.Select(k => $"'{k.Replace("'", "''")}'"));
            c.Execute($"DELETE FROM Pages WHERE document_key IN ({inList})", transaction: tx);
            c.Execute($"DELETE FROM SearchIndex WHERE document_key IN ({inList})", transaction: tx);
        }
        c.Execute("DELETE FROM Documents WHERE team_id=@teamId", new { teamId }, tx);
        c.Execute("DELETE FROM SearchIndex WHERE team_id=@teamId AND entity_type='document'", new { teamId }, tx);
        var now = DateTime.UtcNow.ToString("o");
        foreach (var d in docs)
        {
            c.Execute("INSERT INTO Documents(key,project_id,project_name,team_id,name,url,last_synced) VALUES(@Key,@ProjectId,@ProjectName,@TeamId,@Name,@Url,@LastSynced)",
                new { d.Key, d.ProjectId, d.ProjectName, d.TeamId, d.Name, d.Url, LastSynced = now }, tx);
            c.Execute("INSERT INTO SearchIndex(entity_type,entity_id,display_name,team_id,document_key) VALUES('document',@key,@name,@teamId,@key)",
                new { key = d.Key, name = d.Name, teamId }, tx);
        }
        foreach (var p in pages)
        {
            c.Execute("INSERT OR REPLACE INTO Pages(id,document_key,name,url) VALUES(@Id,@DocumentKey,@Name,@Url)", p, tx);
            c.Execute("INSERT INTO SearchIndex(entity_type,entity_id,display_name,team_id,document_key) VALUES('page',@id,@name,@teamId,@docKey)",
                new { id = p.Id, name = p.Name, teamId, docKey = p.DocumentKey }, tx);
        }
        tx.Commit();
        SetLastSyncTime(DateTime.UtcNow);
    }

    // ── Incremental sync: upsert a single file and its pages atomically ──
    /// <summary>
    /// Upserts one document + its pages. Safe to call multiple times (idempotent).
    /// Called per-file so progress is saved immediately; if sync crashes, already-done
    /// files are skipped on the next run.
    /// </summary>
    public void UpsertFileData(FigmaFile doc, List<FigmaPage> pages)
    {
        var c   = Conn();
        var now = DateTime.UtcNow.ToString("o");
        using var tx = c.BeginTransaction();

        // Upsert document
        c.Execute(@"INSERT INTO Documents(key,project_id,project_name,team_id,name,url,last_synced)
                    VALUES(@Key,@ProjectId,@ProjectName,@TeamId,@Name,@Url,@LastSynced)
                    ON CONFLICT(key) DO UPDATE SET
                        project_id=excluded.project_id, project_name=excluded.project_name,
                        name=excluded.name, url=excluded.url, last_synced=excluded.last_synced",
            new { doc.Key, doc.ProjectId, doc.ProjectName, doc.TeamId, doc.Name, doc.Url, LastSynced = now }, tx);

        // Upsert search index entry for document (delete+insert to keep FTS consistent)
        c.Execute("DELETE FROM SearchIndex WHERE entity_type='document' AND entity_id=@key", new { key = doc.Key }, tx);
        c.Execute("INSERT INTO SearchIndex(entity_type,entity_id,display_name,team_id,document_key) VALUES('document',@key,@name,@teamId,@key)",
            new { key = doc.Key, name = doc.Name, teamId = doc.TeamId }, tx);

        // Replace pages for this file
        c.Execute("DELETE FROM Pages WHERE document_key=@key", new { key = doc.Key }, tx);
        c.Execute("DELETE FROM SearchIndex WHERE entity_type='page' AND document_key=@key", new { key = doc.Key }, tx);
        foreach (var p in pages)
        {
            // INSERT OR REPLACE handles the case where the same page id appears in multiple files
            c.Execute("INSERT OR REPLACE INTO Pages(id,document_key,name,url) VALUES(@Id,@DocumentKey,@Name,@Url)", p, tx);
            c.Execute("INSERT INTO SearchIndex(entity_type,entity_id,display_name,team_id,document_key) VALUES('page',@id,@name,@teamId,@docKey)",
                new { id = p.Id, name = p.Name, teamId = doc.TeamId, docKey = doc.Key }, tx);
        }

        tx.Commit();
    }

    /// <summary>
    /// Returns set of file keys already synced for this team (used to skip on resume).
    /// </summary>
    public HashSet<string> GetSyncedFileKeys(string teamId) =>
        Conn().Query<string>("SELECT key FROM Documents WHERE team_id=@teamId", new { teamId }).ToHashSet();

    /// <summary>
    /// Removes files (and their pages/index) that no longer exist in Figma.
    /// Called after a full team sync completes to clean up deleted files.
    /// </summary>
    public void RemoveDeletedFiles(string teamId, HashSet<string> currentFileKeys)
    {
        var existing = GetSyncedFileKeys(teamId);
        var toDelete = existing.Except(currentFileKeys).ToList();
        if (toDelete.Count == 0) return;

        var c = Conn(); using var tx = c.BeginTransaction();
        var inList = string.Join(",", toDelete.Select(k => $"'{k.Replace("'", "''")}'"));
        c.Execute($"DELETE FROM Pages       WHERE document_key IN ({inList})", transaction: tx);
        c.Execute($"DELETE FROM SearchIndex WHERE document_key IN ({inList})", transaction: tx);
        c.Execute($"DELETE FROM Documents   WHERE key           IN ({inList})", transaction: tx);
        c.Execute($"DELETE FROM SearchIndex WHERE entity_type='document' AND entity_id IN ({inList})", transaction: tx);
        tx.Commit();
    }
    public (HashSet<string> docKeys, HashSet<string> pageIds) SearchRaw(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return (new(), new());
        var c = Conn();
        try
        {
            var safe = keyword.Replace("\"", "\\\"");
            var fts  = $"\\\"{safe}\\\"*";
            var sql  = $"SELECT entity_type, entity_id FROM SearchIndex WHERE display_name MATCH '{fts.Replace("'","''")}' ORDER BY rank";
            var hits = c.Query<(string entity_type, string entity_id)>(sql).ToList();
            return (
                hits.Where(h => h.entity_type == "document").Select(h => h.entity_id).ToHashSet(),
                hits.Where(h => h.entity_type == "page").Select(h => h.entity_id).ToHashSet()
            );
        }
        catch
        {
            var pat = $"%{keyword}%";
            return (
                c.Query<string>("SELECT key FROM Documents WHERE name LIKE @pat", new { pat }).ToHashSet(),
                c.Query<string>("SELECT id FROM Pages WHERE name LIKE @pat", new { pat }).ToHashSet()
            );
        }
    }

    public List<FigmaFile> GetDocumentsByKeys(IEnumerable<string> keys)
    {
        var ks = keys.ToList(); if (ks.Count == 0) return new();
        var inList = string.Join(",", ks.Select(k => $"'{k.Replace("'","''")}'"));
        return Conn().Query<FigmaFile>($"SELECT key Key, project_id ProjectId, project_name ProjectName, team_id TeamId, name Name, url Url FROM Documents WHERE key IN ({inList})").ToList();
    }

    public List<FigmaPage> GetPagesByIds(IEnumerable<string> ids)
    {
        var il = ids.ToList(); if (il.Count == 0) return new();
        var inList = string.Join(",", il.Select(i => $"'{i.Replace("'","''")}'"));
        return Conn().Query<FigmaPage>($"SELECT id Id, document_key DocumentKey, name Name, url Url FROM Pages WHERE id IN ({inList})").ToList();
    }

    public int GetDocumentCount() => Conn().ExecuteScalar<int>("SELECT COUNT(*) FROM Documents");
    public int GetPageCount()     => Conn().ExecuteScalar<int>("SELECT COUNT(*) FROM Pages");
    public DateTime? GetLastSyncTime() { var v = GetSetting("last_sync_time"); return DateTime.TryParse(v, out var dt) ? dt : null; }
    public void SetLastSyncTime(DateTime dt) => SetSetting("last_sync_time", dt.ToString("o"));

    public void Dispose() { _conn?.Close(); _conn?.Dispose(); _conn = null; }
}
