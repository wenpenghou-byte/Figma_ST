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

    private async Task<JsonElement> GetAsync(string url, string token)
    {
        _http.DefaultRequestHeaders.Remove("X-Figma-Token");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Figma-Token", token);
        var resp = await _http.GetAsync(url);
        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
            throw new FigmaAuthException("API Key API Key 无效或已过期，请在设置中更新");
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json).RootElement;
    }

    public async Task<bool> ValidateTokenAsync(string token)
    {
        try { await GetAsync($"{BASE}/me", token); return true; }
        catch (FigmaAuthException) { return false; }
        catch { return false; }
    }

    /// <summary>
    /// Syncs all teams. After each team completes, calls onTeamSynced(teamId, docs, pages)
    /// so the caller can persist data immediately (e.g. DatabaseService.ReplaceTeamData).
    /// </summary>
    public async Task SyncAllTeamsAsync(
        List<TeamConfig> teams,
        string token,
        IProgress<SyncProgress> progress,
        Action<string, List<FigmaFile>, List<FigmaPage>>? onTeamSynced = null,
        CancellationToken ct = default)
    {
        for (int ti = 0; ti < teams.Count; ti++)
        {
            ct.ThrowIfCancellationRequested();
            var team = teams[ti];

            progress.Report(new SyncProgress
            {
                Phase    = "正在获取项目列表",
                TeamName = team.DisplayName,
                Current  = ti + 1,
                Total    = teams.Count,
                Detail   = team.DisplayName
            });

            var teamDocs  = new List<FigmaFile>();
            var teamPages = new List<FigmaPage>();

            var projectsDoc = await GetAsync($"{BASE}/teams/{team.TeamId}/projects", token);
            var projects = projectsDoc.GetProperty("projects").EnumerateArray().ToList();

            for (int pi = 0; pi < projects.Count; pi++)
            {
                ct.ThrowIfCancellationRequested();
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

                var filesDoc = await GetAsync($"{BASE}/projects/{projId}/files", token);
                var files = filesDoc.GetProperty("files").EnumerateArray().ToList();

                for (int fi = 0; fi < files.Count; fi++)
                {
                    ct.ThrowIfCancellationRequested();
                    var file     = files[fi];
                    var fileKey  = file.GetProperty("key").GetString() ?? "";
                    var fileName = file.GetProperty("name").GetString() ?? "";
                    var fileUrl  = $"https://www.figma.com/file/{fileKey}";

                    progress.Report(new SyncProgress
                    {
                        Phase    = "正在获取页面信息",
                        TeamName = team.DisplayName,
                        Current  = fi + 1,
                        Total    = files.Count,
                        Detail   = fileName
                    });

                    teamDocs.Add(new FigmaFile
                    {
                        Key         = fileKey,
                        ProjectId   = projId,
                        ProjectName = projName,
                        TeamId      = team.TeamId,
                        Name        = fileName,
                        Url         = fileUrl
                    });

                    // Fetch pages
                    try
                    {
                        var detail   = await GetAsync($"{BASE}/files/{fileKey}?depth=1", token);
                        var children = detail.GetProperty("document").GetProperty("children").EnumerateArray();
                        foreach (var page in children)
                        {
                            var pageId   = page.GetProperty("id").GetString() ?? "";
                            var pageName = page.GetProperty("name").GetString() ?? "";
                            var nodeId   = pageId.Replace(":", "%3A");
                            teamPages.Add(new FigmaPage
                            {
                                Id          = pageId,
                                DocumentKey = fileKey,
                                Name        = pageName,
                                Url         = $"{fileUrl}?node-id={nodeId}"
                            });
                        }
                    }
                    catch (Exception ex) when (ex is not FigmaAuthException) { /* skip pages on error */ }

                    await Task.Delay(200, ct); // respect rate limits
                }
            }

            // Persist this team's data immediately after it finishes
            onTeamSynced?.Invoke(team.TeamId, teamDocs, teamPages);

            progress.Report(new SyncProgress
            {
                Phase    = $"已完成 {team.DisplayName}",
                TeamName = team.DisplayName,
                Current  = ti + 1,
                Total    = teams.Count,
                Detail   = $"{teamDocs.Count} 个文档, {teamPages.Count} 个页面"
            });
        }

        progress.Report(new SyncProgress
        {
            Phase   = "全部完成",
            Current = teams.Count,
            Total   = teams.Count
        });
    }
}
