using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace FigmaSearch.Services;

/// <summary>
/// Calls the BrainMaker (NetEase internal) AI chat API.
/// Flow: Auth Account + Auth Key → temporary Token → chat request.
/// The chat endpoint returns an OpenAI-compatible SSE stream where each
/// chunk carries a delta content fragment in choices[0].delta.content.
/// </summary>
public class BrainMakerService : IDisposable
{
    private readonly HttpClient _http;
    private const string AUTH_URL = "http://auth.nie.netease.com/api/v2/tokens";
    private const string CHAT_BASE_URL = "https://ext-idc-ai.nie.netease.com/api/v1/docsets/";

    private string? _cachedToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    // Auth credentials (set from settings)
    private string _authAccount = "";
    private string _authKey = "";
    private string _userCorp = "";
    private string _project = "space_houwenpeng";
    private string _docset = "docset_houwenpeng";

    public BrainMakerService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        _http.DefaultRequestHeaders.Add("User-Agent", "FigmaSearch/2.0");
    }

    /// <summary>
    /// Update credentials from settings. Call after loading/saving settings.
    /// </summary>
    public void Configure(string authAccount, string authKey, string userCorp, string project = "space_houwenpeng", string docset = "docset_houwenpeng")
    {
        _authAccount = authAccount;
        _authKey = authKey;
        _userCorp = userCorp;
        _project = string.IsNullOrWhiteSpace(project) ? "space_houwenpeng" : project;
        _docset = string.IsNullOrWhiteSpace(docset) ? "docset_houwenpeng" : docset;
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
    /// Send a question to the BrainMaker AI and return the full response text.
    /// The API returns an SSE stream; this method consumes all chunks and
    /// concatenates the delta content fragments into a single string.
    /// Throws on auth failure, network error, or unexpected response.
    /// </summary>
    public async Task<string> ChatAsync(string input, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("BrainMaker 未配置，请在设置中填写 Auth Account、Auth Key 和 User Corp。");

        var token = await GetTokenAsync(ct);
        var chatUrl = $"{CHAT_BASE_URL}@{_docset}:chat";
        var body = JsonSerializer.Serialize(new
        {
            input = input,
            use_dataset_config = true
        });

        var resp = await SendChatRequestAsync(chatUrl, token, body, ct);

        // If 401/403, token may have expired — retry once with fresh token
        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            resp.Dispose();
            _cachedToken = null;
            _tokenExpiry = DateTime.MinValue;
            token = await GetTokenAsync(ct);
            resp = await SendChatRequestAsync(chatUrl, token, body, ct);
        }

        if (!resp.IsSuccessStatusCode)
        {
            var errorText = await resp.Content.ReadAsStringAsync(ct);
            resp.Dispose();
            throw new HttpRequestException($"BrainMaker API 返回错误 ({(int)resp.StatusCode}): {Truncate(errorText, 200)}");
        }

        // Read SSE stream and concatenate delta content
        using (resp)
        {
            return await ReadSseResponseAsync(resp, ct);
        }
    }

    private async Task<HttpResponseMessage> SendChatRequestAsync(string chatUrl, string token, string body, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, chatUrl);
        request.Headers.Add("X-Auth-User", _userCorp);
        request.Headers.Add("X-Auth-Project", _project);
        request.Headers.Add("X-Access-Token", token);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        // Use ResponseHeadersRead so we can stream the body
        return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    /// <summary>
    /// Reads an SSE (Server-Sent Events) response stream.
    /// Each line is "data: {json}" where json has choices[0].delta.content.
    /// Concatenates all content fragments into the final answer.
    /// </summary>
    private static async Task<string> ReadSseResponseAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        var sb = new StringBuilder();
        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line == null) break; // end of stream

            // SSE format: "data: {...}" or "data: [DONE]"
            if (!line.StartsWith("data: ", StringComparison.Ordinal))
                continue;

            var payload = line.Substring(6).Trim();
            if (payload == "[DONE]")
                break;

            // Parse the JSON chunk and extract delta content
            try
            {
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;

                // OpenAI-compatible: choices[0].delta.content
                if (root.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0)
                {
                    var firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("delta", out var delta) &&
                        delta.TryGetProperty("content", out var content))
                    {
                        var text = content.GetString();
                        if (!string.IsNullOrEmpty(text))
                            sb.Append(text);
                    }
                }
            }
            catch (JsonException)
            {
                // Malformed chunk — skip
            }
        }

        var result = sb.ToString().Trim();
        if (string.IsNullOrEmpty(result))
            throw new InvalidOperationException("AI 未返回有效回答，请稍后重试。");

        return result;
    }

    /// <summary>
    /// Gets a temporary access token from the NetEase Auth service.
    /// Caches the token for 23 hours (tokens last 24 hours by default per Auth docs).
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
        _tokenExpiry = DateTime.UtcNow.AddHours(23); // token valid 24h, refresh 1h early
        return token;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    public void Dispose() => _http.Dispose();
}
