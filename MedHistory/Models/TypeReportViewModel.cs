namespace MedHistory.Models;

/// <summary>
/// One type's history: the selector row of every type (so switching types is one click), and,
/// once a type is chosen, its entries grouped by day, newest day first, paged in blocks of
/// whole days — see <see cref="Services.TypeReportRules"/>.
/// </summary>
public class TypeReportViewModel
{
    /// <summary>Every entry type, active and not, in the same order the /types page lists them.</summary>
    public required IReadOnlyList<string> AllTypeNames { get; init; }

    /// <summary>Null on the bare selector page (GET /type-report) — no type chosen yet.</summary>
    public string? CurrentType { get; init; }

    public IReadOnlyList<TypeReportDayViewModel> Days { get; init; } = [];

    public int Page { get; init; } = 1;

    /// <summary>Zero when the type has nothing logged — see <see cref="Services.TypeReportRules.PageCount"/>.</summary>
    public int PageCount { get; init; }

    public bool HasNewerPage => Page > 1;

    public bool HasOlderPage => Page < PageCount;
}

/// <summary>One local day's worth of one type's entries, ascending by time within the day.</summary>
public class TypeReportDayViewModel
{
    public required DateOnly Day { get; init; }

    public required IReadOnlyList<DayEntryViewModel> Entries { get; init; }
}
