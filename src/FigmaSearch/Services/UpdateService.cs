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
    private const string RELEASES_URL = "https://api.github.com/repos/wenpenghou-byte/Figma_ST/releases/latest";
    private readonly HttpClient _http;

    public UpdateService()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("User-Agent", "FigmaSearch/1.0");
        _http.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
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
            var resp = await _http.GetStringAsync(RELEASES_URL);
            using var doc = JsonDocument.Parse(resp);
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString()?.TrimStart('v') ?? "";
            var assets = root.GetProperty("assets").EnumerateArray().ToList();
            var exeAsset = assets.FirstOrDefault(a =>
                a.GetProperty("name").GetString()?.EndsWith(".exe") == true);
            var dlUrl = exeAsset.ValueKind != JsonValueKind.Undefined
                ? exeAsset.GetProperty("browser_download_url").GetString() ?? ""
                : $"https://github.com/wenpenghou-byte/Figma_ST/releases/latest/download/FigmaSearch_Setup.exe";

            var isNewer = CompareVersions(tag, CurrentVersion()) > 0;
            return new UpdateInfo { LatestVersion = tag, DownloadUrl = dlUrl, IsNewer = isNewer };
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
            var va = Version.Parse(a.Contains(".") ? a : a + ".0");
            var vb = Version.Parse(b.Contains(".") ? b : b + ".0");
            return va.CompareTo(vb);
        }
        catch { return string.Compare(a, b, StringComparison.Ordinal); }
    }

    public async Task<string> DownloadInstallerAsync(string downloadUrl,
        IProgress<int> progress, CancellationToken ct = default)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "FigmaSearch_Update.exe");
        using var resp = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
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
