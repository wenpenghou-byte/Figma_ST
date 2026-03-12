namespace FigmaSearch.Models;

public class SearchResultItem
{
    public SearchResultItemType ItemType { get; set; }
    public string DocumentKey { get; set; } = string.Empty;
    public string DocumentName { get; set; } = string.Empty;
    public string DocumentUrl { get; set; } = string.Empty;
    public string TeamId { get; set; } = string.Empty;
    public string TeamDisplayName { get; set; } = string.Empty;
    public bool IsDocumentNameMatched { get; set; }
    public string PageId { get; set; } = string.Empty;
    public string PageName { get; set; } = string.Empty;
    public string PageUrl { get; set; } = string.Empty;
    public bool IsPageNameMatched { get; set; }
    public List<SearchResultItem> ChildPages { get; set; } = new();
    public bool HasMatchedChildren => ChildPages.Count > 0;
    public bool IsExpanded { get; set; } = true;
}

public enum SearchResultItemType { DocumentHeader, Page }

public class SearchResultGroup
{
    public string TeamId { get; set; } = string.Empty;
    public string TeamDisplayName { get; set; } = string.Empty;
    public List<SearchResultItem> Items { get; set; } = new();
}

public class SyncProgress
{
    public string Phase { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public int Current { get; set; }
    public int Total { get; set; }
    public string Detail { get; set; } = string.Empty;
    public double Percentage => Total == 0 ? 0 : (double)Current / Total * 100;
}
