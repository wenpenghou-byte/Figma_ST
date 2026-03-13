namespace FigmaSearch.Models;

public class FigmaFile
{
    public string Key { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string TeamId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime? LastSynced { get; set; }
    /// <summary>Figma's last_modified timestamp string — used to detect file changes without re-fetching pages.</summary>
    public string FigmaLastModified { get; set; } = string.Empty;
}
