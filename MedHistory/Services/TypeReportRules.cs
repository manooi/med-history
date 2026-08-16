namespace MedHistory.Services;

/// <summary>
/// Pure type-report rules — no clock, no database, no HTTP. The type report is a chosen set of
/// types' history across every day any of them was logged, newest day first, paged in blocks of
/// whole days rather than whole entries: a day with five entries and a day with one both cost one
/// slot in a page, so the page always lines up with a clean run of calendar days.
///
/// Everything here works on days and generic rows the caller has already resolved — which local
/// day an entry falls on comes from <see cref="AppTime.DayOf"/>, and which entries belong to
/// which type comes from the database query. This file only ever decides which types the URL
/// selects, and how the resulting days are counted, clamped, sliced into pages, and grouped for
/// display.
/// </summary>
public static class TypeReportRules
{
    /// <summary>Distinct entry-days per page, not entries per page — see the type header.</summary>
    public const int PerPage = 30;

    /// <summary>
    /// The requested type names resolved to the spelling stored in <c>EntryTypes</c>, unknown
    /// names dropped, duplicates collapsed, ordered by <paramref name="allTypeNames"/>'s own
    /// display order rather than the order they were asked for.
    ///
    /// Matching is case-insensitive (<see cref="EntryTypeRules.NamesMatch"/>) like every other
    /// type lookup in the app, but the entries are queried by the stored row's own casing —
    /// <c>Entry.Type</c> is compared ordinal, so a hand-typed URL must not be able to miss a type
    /// by casing alone. Fixing the order here is what gives one selection exactly one URL: the
    /// same two types picked in either order canonicalise to the same list, so
    /// <see cref="NeedsCanonicalRedirect"/> can send every other spelling of that selection to it
    /// and the page never renders under two addresses.
    ///
    /// Walking <paramref name="allTypeNames"/> rather than the request is what does all four jobs
    /// at once — a name nobody asked for is skipped, a name asked for twice is still visited once,
    /// and what comes out is a sublist of the display order.
    /// </summary>
    public static IReadOnlyList<string> CanonicalizeTypes(
        IEnumerable<string?> requested, IReadOnlyList<string> allTypeNames)
    {
        var asked = requested.ToList();

        return allTypeNames
            .Where(name => asked.Any(request => EntryTypeRules.NamesMatch(request, name)))
            .ToList();
    }

    /// <summary>
    /// The selection with <paramref name="type"/> removed if it is already in it, added if it is
    /// not — the href behind one checkbox in the selector row, which is the only way the selection
    /// ever changes (there is no form and no Apply button).
    ///
    /// <paramref name="allTypeNames"/> is needed because adding is not an append: the result has
    /// to come back in display order, or ticking the same two types in a different order would
    /// produce a second URL for one selection and bounce through
    /// <see cref="NeedsCanonicalRedirect"/>. Running the toggled list back through
    /// <see cref="CanonicalizeTypes"/> re-sorts it and, for free, keeps a stale name in the
    /// current selection from surviving the toggle.
    /// </summary>
    public static IReadOnlyList<string> ToggleType(
        IReadOnlyList<string> selected, string type, IReadOnlyList<string> allTypeNames)
    {
        var next = selected.Any(name => EntryTypeRules.NamesMatch(name, type))
            ? selected.Where(name => !EntryTypeRules.NamesMatch(name, type))
            : selected.Append(type);

        return CanonicalizeTypes(next, allTypeNames);
    }

    /// <summary>
    /// Whether a request asked for its selection under a spelling that is not the canonical one —
    /// an unknown name that got dropped, a duplicate that got collapsed, wrong casing, or the
    /// right types in the wrong order. True means redirect to <see cref="Href"/> of
    /// <paramref name="canonical"/> before querying anything, the same spirit as the page clamp:
    /// what the URL says and what the page shows never disagree.
    ///
    /// The comparison is ordinal and order-sensitive on purpose — case and order are exactly two
    /// of the four things being normalised, so a comparison forgiving of either would let those
    /// URLs render as-is.
    /// </summary>
    public static bool NeedsCanonicalRedirect(IEnumerable<string?> requested, IReadOnlyList<string> canonical)
    {
        var asked = requested.ToList();

        if (asked.Count != canonical.Count)
        {
            return true;
        }

        for (var i = 0; i < asked.Count; i++)
        {
            if (!string.Equals(asked[i], canonical[i], StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The report's URL for a selection and a page. One builder for the selector's checkboxes, the
    /// pagination links and every redirect target, so a link can never point at a URL the
    /// canonical redirect would immediately bounce — which would be a redirect loop, not a
    /// cosmetic bug.
    ///
    /// Types ride as a repeated <c>types</c> query parameter rather than one comma-joined path
    /// segment: names are user free text, so any joining character is a name a user may legally
    /// type. <c>page=1</c> is left off so the first page has one address, and an empty selection
    /// drops the page entirely — the bare selector page has nothing to page through, so clearing
    /// the selection from page 3 must not leave a page number pointing at nothing.
    /// </summary>
    public static string Href(IReadOnlyList<string> types, int page = 1)
    {
        if (types.Count == 0)
        {
            return "/type-report";
        }

        var parts = types.Select(type => $"types={Uri.EscapeDataString(type)}").ToList();

        if (page > 1)
        {
            parts.Add($"page={page}");
        }

        return $"/type-report?{string.Join("&", parts)}";
    }

    /// <summary>
    /// How many pages a day count spans. Zero days is zero pages, not one: a type with nothing
    /// logged has no page to be "page 1" of, which is what lets <see cref="ClampPage"/> tell an
    /// empty type apart from an out-of-range request into a real one.
    /// </summary>
    public static int PageCount(int dayCount) =>
        dayCount <= 0 ? 0 : (dayCount + PerPage - 1) / PerPage;

    /// <summary>
    /// The nearest page that actually exists. A day count of zero has no valid page at all, so
    /// it clamps to 1 anyway — there is nothing to redirect toward except back to the type's own
    /// (empty) first page. Otherwise <paramref name="requested"/> is pinned into [1, pageCount],
    /// which is what turns a stale or hand-typed page number into the closest real one rather
    /// than a blank read or a crash.
    /// </summary>
    public static int ClampPage(int requested, int pageCount) =>
        pageCount <= 0 ? 1 : Math.Clamp(requested, 1, pageCount);

    /// <summary>
    /// The page's slice of days out of the full newest-first list. <paramref name="page"/> is
    /// assumed already clamped by <see cref="ClampPage"/> — this only does the arithmetic, so
    /// the same slice can be taken of a list of bare <see cref="DateOnly"/> values (to decide the
    /// query range) or of anything else keyed the same way.
    /// </summary>
    public static IReadOnlyList<T> SelectDays<T>(IReadOnlyList<T> daysNewestFirst, int page) =>
        daysNewestFirst.Skip((page - 1) * PerPage).Take(PerPage).ToList();

    /// <summary>
    /// Buckets entries by local day, newest day first; within a day, entries come out in exactly
    /// the order the day page itself renders them — <see cref="EntryRules.OrderEntries{T}"/>,
    /// ascending by time and tie-broken by type name — just carried across many days instead of
    /// one. Ordering before grouping is what makes each day's own entries come out that way too:
    /// <c>GroupBy</c> preserves the order items were seen in, it does not reorder them.
    ///
    /// The tie-break earns its keep here because a page of this report spans however many types
    /// the URL selected, so two entries at the same instant genuinely can be of different types;
    /// deferring to <see cref="EntryRules.OrderEntries{T}"/> is what keeps that answer identical
    /// to the day page's rather than a second, nearly-equal ordering rule.
    /// </summary>
    public static IReadOnlyList<IGrouping<DateOnly, T>> GroupByDayDescending<T>(
        IEnumerable<T> entries,
        Func<T, DateOnly> localDaySelector,
        Func<T, DateTimeOffset> occurredAtSelector,
        Func<T, string> typeSelector) =>
        EntryRules.OrderEntries(entries, occurredAtSelector, typeSelector)
            .GroupBy(localDaySelector)
            .OrderByDescending(group => group.Key)
            .ToList();
}
