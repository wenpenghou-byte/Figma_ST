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
    /// Incremental sync: fetches all teams' files but only re-fetches pages for files
    /// whose Figma last_modified timestamp has changed since the last sync.
    /// Unchanged files get a lightweight metadata upsert (name/URL) but skip the
    /// expensive per-file GET /files/{key}?depth=1 call.
    ///
    /// - If a file's page fetch fails (403, network, etc.), existing DB pages are preserved.
    /// - Cancellation is only checked between files (never mid-file), so every started
    ///   file is guaranteed to be fully persisted.
    /// - Files are sorted last_modified descending so recently-changed files are first.
    /// </summary>
    /// <param name="getSyncedModified">
    /// Returns a dict of (file_key → stored figma_last_modified) for a given team.
    /// When provided, files whose last_modified matches the stored value are skipped
    /// (metadata is still upserted, but pages are not re-fetched).
    /// Pass null to disable incremental logic (full re-sync).
    /// </param>
    public async Task SyncAllTeamsAsync(
        List<TeamConfig> teams,
        string globalToken,
        IProgress<SyncProgress> progress,
        Func<string, bool>? hasPages = null,
        Action<FigmaFile, List<FigmaPage>?>? onFileSynced = null,
        Action<string, HashSet<string>>? onTeamFinished = null,
        Func<string, Dictionary<string, string>>? getSyncedModified = null,
        CancellationToken ct = default)
    {
        for (int ti = 0; ti < teams.Count; ti++)
        {
            // Check cancel only between teams — never mid-file
            if (ct.IsCancellationRequested) return;
            var team = teams[ti];

            // Use per-team API key if configured, otherwise fall back to global key
            var token = !string.IsNullOrEmpty(team.ApiKey) ? team.ApiKey : globalToken;

            progress.Report(new SyncProgress
            {
                Phase    = "正在获取项目列表",
                TeamName = team.DisplayName,
                Current  = ti + 1,
                Total    = teams.Count,
                Detail   = team.DisplayName
            });

            var teamFileKeys = new HashSet<string>(); // tracks all file keys seen for this team
            int syncedFiles = 0;
            int skippedTotal = 0; // files skipped because last_modified unchanged
            var failedFileNames = new List<string>(); // collect failed file names for user visibility
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

                // Load stored last_modified map for this team once per project loop
                // (lazy: only when incremental sync is enabled)
                Dictionary<string, string>? storedModifiedMap = getSyncedModified?.Invoke(team.TeamId);

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

                    // ── Incremental sync: skip page fetch if file unchanged ──────────
                    // Compare Figma's last_modified with what we stored in the DB.
                    // If they match AND the file already has pages, just upsert metadata
                    // (name/URL/last_modified) and move on — no GET /files/{key} call.
                    bool dbHasPages = hasPages?.Invoke(fileKey) ?? true;
                    if (storedModifiedMap != null
                        && !string.IsNullOrEmpty(lastModified)
                        && storedModifiedMap.TryGetValue(fileKey, out var storedLm)
                        && storedLm == lastModified
                        && dbHasPages)
                    {
                        // File content unchanged — upsert metadata only (pages = null keeps existing)
                        onFileSynced?.Invoke(doc, null);
                        skippedTotal++;
                        syncedFiles++;

                        // Rate limit delay (shorter for skipped files)
                        try { await Task.Delay(20, ct); }
                        catch (OperationCanceledException) { /* that's fine */ }
                        continue;
                    }

                    progress.Report(new SyncProgress
                    {
                        Phase    = "正在获取页面信息",
                        TeamName = team.DisplayName,
                        Current  = fi + 1,
                        Total    = files.Count,
                        Detail   = fileName
                    });

                    // Fetch pages with retry (up to 3 attempts with exponential back-off).
                    // Also force retry for files that exist in DB but have zero pages
                    // (leftover from a previous sync where page-fetch silently failed).
                    var filePages = new List<FigmaPage>();
                    bool pagesFetchOk = false;
                    const int maxPageAttempts = 3;

                    for (int attempt = 1; attempt <= maxPageAttempts && !pagesFetchOk; attempt++)
                    {
                        try
                        {
                            // throwOnForbidden:false — some files may not be accessible; skip instead of aborting
                            // Do NOT pass ct here — once we started a file, let it finish
                            var detail   = await GetAsync($"{BASE}/files/{fileKey}?depth=1", token, throwOnForbidden: false);
                            var children = detail.GetProperty("document").GetProperty("children").EnumerateArray();
                            filePages.Clear();
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

                            // Every Figma file has at least 1 page. If the API returned 0 pages,
                            // the token likely lacks sufficient access (view-only on some files
                            // returns an empty children array). Treat this as a failure so we
                            // preserve any previously-synced pages rather than wiping them.
                            if (filePages.Count == 0)
                            {
                                System.Diagnostics.Debug.WriteLine(
                                    $"[FigmaApi] WARNING: API returned 0 pages for {fileName} ({fileKey}) — possible permission issue");
                                if (attempt >= maxPageAttempts)
                                    failedFileNames.Add($"{fileName}(0页)");
                                else
                                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
                                continue; // retry — do NOT set pagesFetchOk
                            }

                            pagesFetchOk = true;
                        }
                        catch (Exception ex) when (ex is not FigmaAuthException)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"[FigmaApi] Attempt {attempt}/{maxPageAttempts} failed to fetch pages for {fileName} ({fileKey}): {ex.Message}");
                            if (attempt < maxPageAttempts)
                            {
                                // Exponential back-off: 2s, 4s
                                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
                            }
                            else
                            {
                                failedFileNames.Add(fileName);
                            }
                        }
                    }

                    // Always persist the document metadata (name, URL) so it's searchable.
                    // Only update pages when they were fetched successfully; otherwise
                    // keep whatever pages are already in the DB.
                    onFileSynced?.Invoke(doc, pagesFetchOk ? filePages : null);
                    syncedFiles++;

                    // Warn about files whose pages were never successfully fetched
                    // (neither now nor in a previous sync)
                    if (!pagesFetchOk && !dbHasPages)
                    {
                        progress.Report(new SyncProgress
                        {
                            Phase    = "页面获取失败",
                            TeamName = team.DisplayName,
                            Current  = fi + 1,
                            Total    = files.Count,
                            Detail   = $"「{fileName}」页面获取失败（已重试 {maxPageAttempts} 次）"
                        });
                    }

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

            string detail2;
            var updatedCount = syncedFiles - skippedTotal;
            var skippedSuffix = skippedTotal > 0 ? $"，跳过 {skippedTotal} 个未变更" : "";
            if (failedFileNames.Count > 0)
            {
                var names = string.Join("、", failedFileNames.Take(5));
                var suffix = failedFileNames.Count > 5 ? $" 等 {failedFileNames.Count} 个文件" : "";
                detail2 = $"更新 {updatedCount} 个文件{skippedSuffix}，{failedFileNames.Count} 个获取失败：{names}{suffix}";
            }
            else
            {
                detail2 = $"更新 {updatedCount} 个文件{skippedSuffix}";
            }
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

