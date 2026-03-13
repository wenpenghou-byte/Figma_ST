using System.IO;
using System.Net.Http;
using System.Reflection;

namespace FigmaSearch.Services;

public class UpdateInfo
{
    public string LatestVersion { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public bool IsNewer { get; set; }
}

public class UpdateService
{
    // Using redirect trick: GitHub /releases/latest redirects to /releases/tag/vX.Y.Z
    // This bypasses the GitHub API entirely, so no auth token or rate-limit issues.
    private const string LATEST_REDIRECT_URL = "https://github.com/wenpenghou-byte/Figma_ST/releases/latest";
    private const string DOWNLOAD_URL = "https://github.com/wenpenghou-byte/Figma_ST/releases/latest/download/FigmaSearch_Setup.exe";

    private readonly HttpClient _http;

    public UpdateService()
    {
        // AllowAutoRedirect=false so we can read the Location header ourselves
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
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
            using var resp = await _http.GetAsync(LATEST_REDIRECT_URL);

            // Expect a 302 redirect whose Location is .../releases/tag/vX.Y.Z
            var location = resp.Headers.Location?.ToString() ?? "";
            if (string.IsNullOrEmpty(location))
            {
                System.Diagnostics.Debug.WriteLine("[UpdateService] No Location header in response.");
                return null;
            }

            // Extract version from the tag segment, e.g. ".../tag/v1.3.5" -> "1.3.5"
            var tag = location.Split('/').LastOrDefault()?.TrimStart('v') ?? "";
            if (string.IsNullOrEmpty(tag))
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateService] Could not parse tag from: {location}");
                return null;
            }

            var isNewer = CompareVersions(tag, CurrentVersion()) > 0;
            return new UpdateInfo
            {
                LatestVersion = tag,
                DownloadUrl   = DOWNLOAD_URL,
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
        // For download we need redirects, so use a separate client
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
