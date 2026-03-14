using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace FigmaSearch.Services;

/// <summary>
/// Calls the BrainMaker (NetEase internal) AI chat API.
/// Flow: Auth Account + Auth Key → temporary Token → chat request.
/// </summary>
public class BrainMakerService : IDisposable
{
    private readonly HttpClient _http;
    private const string AUTH_URL = "http://auth.nie.netease.com/api/v2/tokens";
    private const string CHAT_URL = "https://ext-idc-ai.nie.netease.com/api/v1/docsets/@docset_houwenpeng:chat";

    private string? _cachedToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    // Auth credentials (set from settings)
    private string _authAccount = "";
    private string _authKey = "";
    private string _userCorp = "";
    private string _project = "space_houwenpeng";

    public BrainMakerService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.Add("User-Agent", "FigmaSearch/2.0");
    }

    /// <summary>
    /// Update credentials from settings. Call after loading/saving settings.
    /// </summary>
    public void Configure(string authAccount, string authKey, string userCorp, string project = "space_houwenpeng")
    {
        _authAccount = authAccount;
        _authKey = authKey;
        _userCorp = userCorp;
        _project = string.IsNullOrWhiteSpace(project) ? "space_houwenpeng" : project;
        // Invalidate cached token when credentials change
        _cachedToken = null;
        _tokenExpiry = DateTime.MinValue;
    }

    /// <summary>
    /// Returns true if the service has enough configuration to attempt a chat request.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_authAccount) &&
        !string.IsNullOrWhiteSpace(_authKey) &&
        !string.IsNullOrWhiteSpace(_userCorp);

    /// <summary>
    /// Send a question to the BrainMaker AI and return the response text.
    /// Throws on auth failure, network error, or unexpected response.
    /// </summary>
    public async Task<string> ChatAsync(string input, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("BrainMaker 未配置，请在设置中填写 Auth Account、Auth Key 和 User Corp。");

        var token = await GetTokenAsync(ct);

        var request = new HttpRequestMessage(HttpMethod.Post, CHAT_URL);
        request.Headers.Add("X-Auth-User", _userCorp);
        request.Headers.Add("X-Auth-Project", _project);
        request.Headers.Add("X-Access-Token", token);

        var body = JsonSerializer.Serialize(new
        {
            input = input,
            use_dataset_config = true
        });
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var resp = await _http.SendAsync(request, ct);

        // If 401/403, token may have expired — retry once with fresh token
        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            _cachedToken = null;
            _tokenExpiry = DateTime.MinValue;
            token = await GetTokenAsync(ct);

            request = new HttpRequestMessage(HttpMethod.Post, CHAT_URL);
            request.Headers.Add("X-Auth-User", _userCorp);
            request.Headers.Add("X-Auth-Project", _project);
            request.Headers.Add("X-Access-Token", token);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            resp = await _http.SendAsync(request, ct);
        }

        var responseText = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"BrainMaker API 返回错误 ({(int)resp.StatusCode}): {Truncate(responseText, 200)}");

        // Try to parse structured response, fall back to raw text
        return ParseResponse(responseText);
    }

    /// <summary>
    /// Gets a temporary access token from the NetEase Auth service.
    /// Caches the token for 50 minutes (tokens typically last 1 hour).
    /// </summary>
    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_cachedToken != null && DateTime.UtcNow < _tokenExpiry)
            return _cachedToken;

        var payload = JsonSerializer.Serialize(new
        {
            user = _authAccount,
            key = _authKey
        });

        var resp = await _http.PostAsync(AUTH_URL, new StringContent(payload, Encoding.UTF8, "application/json"), ct);
        var text = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Auth Token 获取失败 ({(int)resp.StatusCode}): {Truncate(text, 200)}");

        using var doc = JsonDocument.Parse(text);
        var token = doc.RootElement.GetProperty("token").GetString()
            ?? throw new InvalidOperationException("Auth 返回的 token 为空");

        _cachedToken = token;
        _tokenExpiry = DateTime.UtcNow.AddMinutes(50); // conservative cache
        return token;
    }

    /// <summary>
    /// Parse the chat API response. Tries common JSON shapes, falls back to raw text.
    /// </summary>
    private static string ParseResponse(string responseText)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;

            // Try common response shapes
            if (root.TryGetProperty("output", out var output))
                return output.GetString() ?? responseText;
            if (root.TryGetProperty("answer", out var answer))
                return answer.GetString() ?? responseText;
            if (root.TryGetProperty("data", out var data))
            {
                if (data.TryGetProperty("output", out var dataOutput))
                    return dataOutput.GetString() ?? responseText;
                if (data.TryGetProperty("answer", out var dataAnswer))
                    return dataAnswer.GetString() ?? responseText;
                if (data.TryGetProperty("text", out var dataText))
                    return dataText.GetString() ?? responseText;
            }
            if (root.TryGetProperty("message", out var msg))
                return msg.GetString() ?? responseText;
            if (root.TryGetProperty("text", out var text))
                return text.GetString() ?? responseText;

            // If it's just a string value
            if (root.ValueKind == JsonValueKind.String)
                return root.GetString() ?? responseText;
        }
        catch
        {
            // Not JSON — return raw
        }

        return responseText;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    public void Dispose() => _http.Dispose();
}
