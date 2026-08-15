namespace MedHistory.Models;

/// <summary>
/// Search over every entry's note and med name. Paged in blocks of distinct entry-days the same
/// way the type report is — see <see cref="Services.TypeReportRules"/>, which this page reuses
/// directly rather than reimplementing its own pagination. Unlike the type report, a search's
/// days can mix every type, so each row also carries its own <see cref="DayEntryViewModel.Type"/>.
/// </summary>
public class SearchViewModel
{
    /// <summary>Normalized query (trimmed, never empty) — null on the bare search form, before any search has run.</summary>
    public string? Query { get; init; }

    /// <summary>Total distinct days the query matched, across every page — not just this page's count.</summary>
    public int MatchedDayCount { get; init; }

    public IReadOnlyList<TypeReportDayViewModel> Days { get; init; } = [];

    public int Page { get; init; } = 1;

    /// <summary>Zero when the query matched nothing — see <see cref="Services.TypeReportRules.PageCount"/>.</summary>
    public int PageCount { get; init; }

    public bool HasNewerPage => Page > 1;

    public bool HasOlderPage => Page < PageCount;
}
