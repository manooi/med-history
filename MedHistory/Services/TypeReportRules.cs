namespace MedHistory.Services;

/// <summary>
/// Pure type-report rules — no clock, no database, no HTTP. The type report is one type's
/// history across every day it was logged, newest day first, paged in blocks of whole days
/// rather than whole entries: a day with five entries and a day with one both cost one slot in
/// a page, so the page always lines up with a clean run of calendar days.
///
/// Everything here works on days and generic rows the caller has already resolved — which local
/// day an entry falls on comes from <see cref="AppTime.DayOf"/>, and which entries belong to
/// which type comes from the database query. This file only ever decides how those days are
/// counted, clamped, sliced into pages, and grouped for display.
/// </summary>
public static class TypeReportRules
{
    /// <summary>Distinct entry-days per page, not entries per page — see the type header.</summary>
    public const int PerPage = 30;

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
    /// Buckets entries by local day, newest day first; within a day, entries come out ascending
    /// by time — the same order the day page itself renders in
    /// (<see cref="EntryRules.OrderEntries{T}"/>), just carried across many days instead of one.
    /// Sorting ascending before grouping is what makes each day's own entries come out ascending
    /// too: <c>GroupBy</c> preserves the order items were seen in, it does not reorder them.
    ///
    /// Ties within a day are not given a secondary tie-break the way
    /// <see cref="EntryRules.OrderEntries{T}"/> breaks ties by type name — every entry passed in
    /// here already shares one type, so there is no type name left to break a tie with, and the
    /// stable sort's original order is as good an answer as any.
    /// </summary>
    public static IReadOnlyList<IGrouping<DateOnly, T>> GroupByDayDescending<T>(
        IEnumerable<T> entries,
        Func<T, DateOnly> localDaySelector,
        Func<T, DateTimeOffset> occurredAtSelector) =>
        entries
            .OrderBy(occurredAtSelector)
            .GroupBy(localDaySelector)
            .OrderByDescending(group => group.Key)
            .ToList();
}
