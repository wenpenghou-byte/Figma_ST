using FigmaSearch.Models;
using FigmaSearch.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FigmaSearch.ViewModels;

public class SearchViewModel : INotifyPropertyChanged
{
    private readonly DatabaseService _db;
    private string _query = "";
    private bool _isLoading;
    private string _errorMessage = "";
    private bool _hasError;

    public ObservableCollection<SearchResultGroup> Results { get; } = new();

    public string Query
    {
        get => _query;
        set { _query = value; OnPropertyChanged(); DoSearch(); }
    }

    public bool IsLoading { get => _isLoading; set { _isLoading = value; OnPropertyChanged(); } }
    public string ErrorMessage { get => _errorMessage; set { _errorMessage = value; OnPropertyChanged(); } }
    public bool HasError { get => _hasError; set { _hasError = value; OnPropertyChanged(); } }

    public SearchViewModel(DatabaseService db)
    {
        _db = db;
    }

    private void DoSearch()
    {
        Results.Clear();
        if (string.IsNullOrWhiteSpace(_query)) return;

        var (docKeys, pageIds) = _db.SearchRaw(_query);
        if (docKeys.Count == 0 && pageIds.Count == 0) return;

        // Collect docs that are parents of matched pages
        var pages = _db.GetPagesByIds(pageIds);
        var parentDocKeys = pages.Select(p => p.DocumentKey).ToHashSet();

        // All doc keys to fetch
        var allDocKeys = docKeys.Union(parentDocKeys).ToHashSet();
        var docs = _db.GetDocumentsByKeys(allDocKeys);

        // Group by team
        var teams = docs.GroupBy(d => d.TeamId).ToList();

        int totalCount = 0;
        foreach (var tg in teams)
        {
            if (totalCount >= 16) break;
            var group = new SearchResultGroup
            {
                TeamId          = tg.Key,
                TeamDisplayName = GetTeamDisplayName(tg.Key)
            };

            foreach (var doc in tg.Take(16 - totalCount))
            {
                var item = new SearchResultItem
                {
                    ItemType             = SearchResultItemType.DocumentHeader,
                    DocumentKey          = doc.Key,
                    DocumentName         = doc.Name,
                    DocumentUrl          = doc.Url,
                    TeamId               = doc.TeamId,
                    TeamDisplayName      = group.TeamDisplayName,
                    IsDocumentNameMatched= docKeys.Contains(doc.Key)
                };

                // Add matched child pages
                var childPages = pages.Where(p => p.DocumentKey == doc.Key).ToList();
                foreach (var page in childPages)
                {
                    item.ChildPages.Add(new SearchResultItem
                    {
                        ItemType         = SearchResultItemType.Page,
                        DocumentKey      = doc.Key,
                        DocumentName     = doc.Name,
                        DocumentUrl      = doc.Url,
                        PageId           = page.Id,
                        PageName         = page.Name,
                        PageUrl          = page.Url,
                        IsPageNameMatched= pageIds.Contains(page.Id)
                    });
                }

                group.Items.Add(item);
                totalCount++;
            }

            if (group.Items.Count > 0)
                Results.Add(group);
        }
    }

    private readonly Dictionary<string, string> _teamNameCache = new();
    private string GetTeamDisplayName(string teamId)
    {
        if (_teamNameCache.TryGetValue(teamId, out var n)) return n;
        var teams = _db.GetTeams();
        foreach (var t in teams) _teamNameCache[t.TeamId] = t.DisplayName;
        return _teamNameCache.TryGetValue(teamId, out var dn) ? dn : teamId;
    }

    public void ClearSearch() { _query = ""; OnPropertyChanged(nameof(Query)); Results.Clear(); }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
