using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace FigmaSearch.Services;

public class UpdateInfo
{
    public string LatestVersion { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public bool IsNewer { get; set; }
}

public class UpdateService
{
    // GitLab API: get latest release for project 36568
    private const string GITLAB_RELEASES_API = "https://gitlab.nie.netease.com/api/v4/projects/36568/releases?per_page=1";
    private const string DOWNLOAD_URL_TEMPLATE = "https://gitlab.nie.netease.com/joker1/figst/-/releases/{0}/downloads/FigmaSearch_Setup.exe";

    private readonly HttpClient _http;

    public UpdateService()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("User-Agent", "FigmaSearch/1.0");
        _http.Timeout = TimeSpan.FromSeconds(10);
    }

    public static string CurrentVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v != null ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.0.0";
    }

    public async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        try
        {
            using var resp = await _http.GetAsync(GITLAB_RELEASES_API);
            if (!resp.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateService] GitLab API returned {resp.StatusCode}");
                return null;
            }

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Response is an array; first element is the latest release
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            {
                System.Diagnostics.Debug.WriteLine("[UpdateService] No releases found.");
                return null;
            }

            var latest = root[0];
            var tag = latest.GetProperty("tag_name").GetString()?.TrimStart('v') ?? "";
            if (string.IsNullOrEmpty(tag))
            {
                System.Diagnostics.Debug.WriteLine("[UpdateService] Could not parse tag_name from response.");
                return null;
            }

            var downloadUrl = string.Format(DOWNLOAD_URL_TEMPLATE, $"v{tag}");
            var isNewer = CompareVersions(tag, CurrentVersion()) > 0;
            return new UpdateInfo
            {
                LatestVersion = tag,
                DownloadUrl   = downloadUrl,
                IsNewer       = isNewer
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateService] CheckForUpdateAsync failed: {ex.Message}");
            return null;
        }
    }

    private static int CompareVersions(string a, string b)
    {
        try
        {
            var va = Version.Parse(a.Contains('.') ? a : a + ".0");
            var vb = Version.Parse(b.Contains('.') ? b : b + ".0");
            return va.CompareTo(vb);
        }
        catch { return string.Compare(a, b, StringComparison.Ordinal); }
    }

    public async Task<string> DownloadInstallerAsync(string downloadUrl,
        IProgress<int> progress, CancellationToken ct = default)
    {
        using var dlClient = new HttpClient();
        dlClient.DefaultRequestHeaders.Add("User-Agent", "FigmaSearch/1.0");

        var tempPath = Path.Combine(Path.GetTempPath(), "FigmaSearch_Update.exe");
        using var resp = await dlClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? -1;
        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var file   = File.Create(tempPath);
        var buf = new byte[81920];
        long read = 0;
        int n;
        while ((n = await stream.ReadAsync(buf, ct)) > 0)
        {
            await file.WriteAsync(buf.AsMemory(0, n), ct);
            read += n;
            if (total > 0) progress.Report((int)(read * 100 / total));
        }
        return tempPath;
    }
}
