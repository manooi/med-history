using MedHistory.Services;

namespace MedHistory.Models;

/// <summary>
/// A chosen set of types' history: the selector row of every type (so ticking one more type in
/// or out is one click), and, once at least one type is chosen, their entries merged into one
/// timeline grouped by day, newest day first, paged in blocks of whole days — see
/// <see cref="Services.TypeReportRules"/>.
/// </summary>
public class TypeReportViewModel
{
    /// <summary>Every entry type, active and not, in the same order the /types page lists them.</summary>
    public required IReadOnlyList<string> AllTypeNames { get; init; }

    /// <summary>
    /// The types this page is showing, in <see cref="AllTypeNames"/>'s display order and spelled
    /// as <c>EntryTypes</c> stores them — see
    /// <see cref="Services.TypeReportRules.CanonicalizeTypes"/>. Empty on the bare selector page
    /// (GET /type-report), where nothing has been ticked yet.
    /// </summary>
    public required IReadOnlyList<string> SelectedTypes { get; init; }

    /// <summary>The selection as one line, for the header and the page title.</summary>
    public string SelectedLabel => string.Join(", ", SelectedTypes);

    /// <summary>
    /// Whether each row shows which type it is. With one type selected the header already says
    /// it and a badge on every row would be noise; from two types up it is the only thing telling
    /// the merged rows apart.
    /// </summary>
    public bool ShowTypeBadges => SelectedTypes.Count > 1;

    public IReadOnlyList<TypeReportDayViewModel> Days { get; init; } = [];

    /// <summary>
    /// Which way this page's rows run inside each day. Absent from the URL means the default —
    /// see <see cref="Services.TypeReportRules.ParseSort"/>.
    /// </summary>
    public TypeReportSort Sort { get; init; } = TypeReportSort.OldestFirst;

    /// <summary>
    /// Where the sort toggle points: this same selection on this same page, in the other order.
    /// Carrying <see cref="Page"/> is the point — flipping the sort re-reads the days already on
    /// screen, it does not navigate away from them.
    /// </summary>
    public string SortToggleHref => TypeReportRules.Href(SelectedTypes, Page, TypeReportRules.Flip(Sort));

    /// <summary>The order the toggle would switch to — a link names its destination, not its origin.</summary>
    public string SortToggleLabel => TypeReportRules.SortLabel(TypeReportRules.Flip(Sort));

    public int Page { get; init; } = 1;

    /// <summary>Zero when the selection has nothing logged — see <see cref="Services.TypeReportRules.PageCount"/>.</summary>
    public int PageCount { get; init; }

    public bool HasNewerPage => Page > 1;

    public bool HasOlderPage => Page < PageCount;
}

/// <summary>
/// One local day's worth of the selected types' entries, ascending by time within the day (ties
/// broken by type name, since the day can hold more than one type) — or that exact order reversed,
/// when the report was asked for <see cref="Services.TypeReportSort.NewestFirst"/>.
/// </summary>
public class TypeReportDayViewModel
{
    public required DateOnly Day { get; init; }

    public required IReadOnlyList<DayEntryViewModel> Entries { get; init; }
}
