using System.Text.RegularExpressions;

namespace FigmaSearch.Models;

public class TeamConfig
{
    public int Id { get; set; }
    public string TeamId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    /// <summary>
    /// Optional per-team API key. When set, this team's sync uses this key
    /// instead of the global one. Useful when the global key has view-only
    /// access and a team member with edit access provides their own token.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Validates and normalizes a Team ID input string.
    /// Returns null if the input is valid (with the cleaned ID in <paramref name="cleaned"/>),
    /// or a user-facing error message describing the problem.
    /// </summary>
    public static string? ValidateTeamId(string raw, out string cleaned)
    {
        cleaned = "";

        if (string.IsNullOrWhiteSpace(raw))
            return "Team ID 不能为空";

        var input = raw.Trim();

        // If user pasted a full Figma team URL, extract the numeric ID automatically.
        // e.g. https://www.figma.com/files/team/1234567890/SomeTeamName
        //      https://www.figma.com/files/1234567890/team/...
        var urlMatch = Regex.Match(input, @"figma\.com/files/(?:team/)?(\d+)");
        if (urlMatch.Success)
        {
            cleaned = urlMatch.Groups[1].Value;
            return null; // valid — extracted from URL
        }

        // Strip a trailing slash or "/TeamName" that users sometimes copy
        // e.g. "1234567890/DesignTeam" → "1234567890"
        var slashIdx = input.IndexOf('/');
        if (slashIdx > 0)
            input = input[..slashIdx];

        input = input.Trim();

        if (input.Length == 0)
            return "Team ID 不能为空";

        if (input.Contains(' '))
            return $"Team ID 不能包含空格，请检查输入：「{raw.Trim()}」";

        if (!Regex.IsMatch(input, @"^\d+$"))
            return $"Team ID 应为纯数字，当前包含非法字符：「{input}」\n可从 Figma 团队页面 URL 中获取（例如 figma.com/files/team/这里的数字）";

        if (input.Length < 5 || input.Length > 30)
            return $"Team ID 长度异常（{input.Length} 位），正常应为 5~30 位数字，请检查是否输入正确";

        cleaned = input;
        return null; // valid
    }
}
