namespace FigmaSearch.Models;

public class TeamConfig
{
    public int Id { get; set; }
    public string TeamId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}
