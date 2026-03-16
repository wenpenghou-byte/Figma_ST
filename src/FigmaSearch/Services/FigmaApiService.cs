using FigmaSearch.Models;
using System.Net.Http;
using System.Text.Json;

namespace FigmaSearch.Services;

public class FigmaAuthException : Exception
{
    public FigmaAuthException(string msg) : base(msg) { }
}

public class FigmaApiService
{
    private readonly HttpClient _http;
    private const string BASE = "https://api.figma.com/v1";

    public FigmaApiService()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("User-Agent", "FigmaSearch/1.0");
    }

    /// <summary>
    /// GET with automatic retry on 429 / 5xx.
    /// – 401/403 on /me → FigmaAuthException (real auth failure)
    /// – 403 on a file endpoint → throws HttpRequestException (caller treats as skip)
    /// – 429: honours Retry-After header (or defaults to 60 s)
    /// – 5xx: exponential back-off starting at 2 s, up to 5 attempts
    /// </summary>
    private async Task<JsonElement> GetAsync(string url, string token,
        bool throwOnForbidden = true, CancellationToken ct = default)
    {
        const int maxAttempts = 5;
        int attempt = 0;
        while (true)
        {
            attempt++;
            _http.DefaultRequestHeaders.Remove("X-Figma-Token");
            _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Figma-Token", token);

            var resp = await _http.GetAsync(url, ct);

            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                throw new FigmaAuthException("API Key 无效或已过期，请在设置中更新");

            // 403: only treat as auth error on the /me endpoint (or when explicitly requested)
            if (resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                if (throwOnForbidden)
                    throw new FigmaAuthException("API Key 无效或已过期，请在设置中更新");
                // For individual file endpoints: no access to this file, just fail naturally
                resp.EnsureSuccessStatusCode(); // will throw HttpRequestException
            }

            // Rate limited — wait then retry
            if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                if (attempt >= maxAttempts) resp.EnsureSuccessStatusCode();
                int wait = 60;
                if (resp.Headers.TryGetValues("Retry-After", out var vals) &&
                    int.TryParse(vals.FirstOrDefault(), out var ra))
                    wait = ra;
                await Task.Delay(TimeSpan.FromSeconds(wait + 2), ct);
                continue;
            }

            // Server error — exponential back-off
            if ((int)resp.StatusCode >= 500)
            {
                if (attempt >= maxAttempts) resp.EnsureSuccessStatusCode();
                var backoff = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                await Task.Delay(backoff, ct);
                continue;
            }

            // Log non-success responses before throwing to aid debugging (e.g. 404 on teams endpoint)
            if (!resp.IsSuccessStatusCode)
            {
                var body = "";
                try { body = await resp.Content.ReadAsStringAsync(ct); } catch { }
                System.Diagnostics.Debug.WriteLine(
                    $"[FigmaApi] HTTP {(int)resp.StatusCode} {resp.StatusCode} for {url} — body: {body}");
            }

            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct);
            return JsonDocument.Parse(json).RootElement;
        }
    }

    public async Task<bool> ValidateTokenAsync(string token)
    {
        try { await GetAsync($"{BASE}/me", token, throwOnForbidden: true); return true; }
        catch (FigmaAuthException) { return false; }
        catch { return false; }
    }

    /// <summary>
    /// Full sync: fetches all teams' files and ALL pages for every file.
    /// Every file's pages are always re-fetched to guarantee completeness.
    /// If a file's page fetch fails (403, network, etc.), existing DB pages are preserved.
    /// Cancellation is only checked between files (never mid-file), so every started
    /// file is guaranteed to be fully persisted.
    /// Files are synced in last_modified descending order so recently-changed
    /// documents are processed first.
    /// </summary>
    public async Task SyncAllTeamsAsync(
        List<TeamConfig> teams,
        string token,
        IProgress<SyncProgress> progress,
        Action<FigmaFile, List<FigmaPage>?>? onFileSynced = null,
        Action<string, HashSet<string>>? onTeamFinished = null,
        CancellationToken ct = default)
    {
        for (int ti = 0; ti < teams.Count; ti++)
        {
            // Check cancel only between teams — never mid-file
            if (ct.IsCancellationRequested) return;
            var team = teams[ti];

            progress.Report(new SyncProgress
            {
                Phase    = "正在获取项目列表",
                TeamName = team.DisplayName,
                Current  = ti + 1,
                Total    = teams.Count,
                Detail   = team.DisplayName
            });

            var teamFileKeys = new HashSet<string>(); // tracks all file keys seen for this team
            int syncedFiles = 0, failedFiles = 0;
            bool teamFullyCompleted = true; // tracks whether ALL files in this team were processed

            var teamUrl = $"{BASE}/teams/{team.TeamId}/projects";
            System.Diagnostics.Debug.WriteLine(
                $"[FigmaApi] Fetching projects for team '{team.DisplayName}' (TeamId='{team.TeamId}'): {teamUrl}");

            JsonElement projectsDoc;
            try
            {
                projectsDoc = await GetAsync(teamUrl, token, throwOnForbidden: true, ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) when (ex is not FigmaAuthException)
            {
                // Re-throw with enriched context so the user / logs can identify which team failed
                throw new HttpRequestException(
                    $"Team「{team.DisplayName}」(ID: {team.TeamId}) 请求失败: {ex.Message}", ex);
            }

            var projects = projectsDoc.GetProperty("projects").EnumerateArray().ToList();

            for (int pi = 0; pi < projects.Count; pi++)
            {
                if (ct.IsCancellationRequested) { teamFullyCompleted = false; break; }

                var proj     = projects[pi];
                var projId   = proj.GetProperty("id").GetRawText().Trim('"');
                var projName = proj.GetProperty("name").GetString() ?? "";

                progress.Report(new SyncProgress
                {
                    Phase    = "正在获取文件列表",
                    TeamName = team.DisplayName,
                    Current  = pi + 1,
                    Total    = projects.Count,
                    Detail   = projName
                });

                JsonElement filesDoc;
                try
                {
                    filesDoc = await GetAsync($"{BASE}/projects/{projId}/files", token, throwOnForbidden: true, ct);
                }
                catch (OperationCanceledException) { teamFullyCompleted = false; break; }

                var files = filesDoc.GetProperty("files").EnumerateArray().ToList();

                // Sort files by last_modified descending — recently changed files first
                files.Sort((a, b) =>
                {
                    var aLm = a.TryGetProperty("last_modified", out var av) ? (av.GetString() ?? "") : "";
                    var bLm = b.TryGetProperty("last_modified", out var bv) ? (bv.GetString() ?? "") : "";
                    return string.Compare(bLm, aLm, StringComparison.Ordinal); // descending
                });

                for (int fi = 0; fi < files.Count; fi++)
                {
                    // Check cancel BEFORE starting a file — once started, always finish
                    if (ct.IsCancellationRequested) { teamFullyCompleted = false; break; }

                    var file     = files[fi];
                    var fileKey      = file.GetProperty("key").GetString() ?? "";
                    var fileName     = file.GetProperty("name").GetString() ?? "";
                    var fileUrl      = $"https://www.figma.com/file/{fileKey}";
                    var lastModified = file.TryGetProperty("last_modified", out var lmProp)
                                       ? (lmProp.GetString() ?? "") : "";

                    teamFileKeys.Add(fileKey);

                    progress.Report(new SyncProgress
                    {
                        Phase    = "正在获取页面信息",
                        TeamName = team.DisplayName,
                        Current  = fi + 1,
                        Total    = files.Count,
                        Detail   = fileName
                    });

                    var doc = new FigmaFile
                    {
                        Key              = fileKey,
                        ProjectId        = projId,
                        ProjectName      = projName,
                        TeamId           = team.TeamId,
                        Name             = fileName,
                        Url              = fileUrl,
                        FigmaLastModified = lastModified
                    };

                    var filePages = new List<FigmaPage>();
                    bool pagesFetchOk = false;
                    try
                    {
                        // throwOnForbidden:false — some files may not be accessible; skip instead of aborting
                        // Do NOT pass ct here — once we started a file, let it finish
                        var detail   = await GetAsync($"{BASE}/files/{fileKey}?depth=1", token, throwOnForbidden: false);
                        var children = detail.GetProperty("document").GetProperty("children").EnumerateArray();
                        foreach (var page in children)
                        {
                            var pageId   = page.GetProperty("id").GetString() ?? "";
                            var pageName = page.GetProperty("name").GetString() ?? "";
                            var nodeId   = pageId.Replace(":", "%3A");
                            filePages.Add(new FigmaPage
                            {
                                Id          = pageId,
                                DocumentKey = fileKey,
                                Name        = pageName,
                                Url         = $"{fileUrl}?node-id={nodeId}"
                            });
                        }
                        pagesFetchOk = true;
                    }
                    catch (Exception ex) when (ex is not FigmaAuthException)
                    {
                        // Could not fetch pages (e.g. 403 view-only, network error).
                        failedFiles++;
                        System.Diagnostics.Debug.WriteLine($"[FigmaApi] Failed to fetch pages for {fileName} ({fileKey}): {ex.Message}");
                    }

                    // Always persist the document metadata (name, URL) so it's searchable.
                    // Only update pages when they were fetched successfully; otherwise
                    // keep whatever pages are already in the DB.
                    onFileSynced?.Invoke(doc, pagesFetchOk ? filePages : null);
                    syncedFiles++;

                    // Rate limit delay — do NOT pass ct (let the write complete cleanly)
                    try { await Task.Delay(200, ct); }
                    catch (OperationCanceledException) { /* delay interrupted, that's fine — file is already saved */ }
                }
            }

            // Only clean up deleted files if we successfully enumerated ALL files in this team.
            // If sync was cancelled mid-team, we might not have seen all files yet —
            // cleaning up would incorrectly delete files we simply haven't reached.
            if (teamFullyCompleted)
            {
                onTeamFinished?.Invoke(team.TeamId, teamFileKeys);
            }

            var detail2 = failedFiles > 0
                ? $"同步 {syncedFiles} 个文件，{failedFiles} 个文件获取失败（已保留旧数据）"
                : $"同步 {syncedFiles} 个文件";
            progress.Report(new SyncProgress
            {
                Phase    = teamFullyCompleted ? $"已完成 {team.DisplayName}" : $"已中断 {team.DisplayName}",
                TeamName = team.DisplayName,
                Current  = ti + 1,
                Total    = teams.Count,
                Detail   = detail2
            });

            if (!teamFullyCompleted) return; // cancelled — stop processing more teams
        }

        progress.Report(new SyncProgress
        {
            Phase   = "全部完成",
            Current = teams.Count,
            Total   = teams.Count
        });
    }
}

