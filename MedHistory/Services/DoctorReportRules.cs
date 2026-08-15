namespace MedHistory.Services;

/// <summary>One entry type's total within the report's date range, in the app's standard type
/// display order — see <see cref="EntryTypeRules.SortForDisplay{T}"/>.</summary>
public readonly record struct DoctorTypeCount(string Type, int Count);

/// <summary>
/// Pure doctor-report rules — no clock, no database, no HTTP. The report is a printable
/// date-range summary for a visit, so most of what needs deciding here is the range itself: what
/// a missing or malformed bound defaults to, what a backwards range means, and how long a range
/// the report will ever try to query in one page. The rest — per-type totals and the voted-day
/// count that make up the header's summary line — are simple shaping the controller would
/// otherwise inline, pulled out here so they stay unit-testable without a database.
/// </summary>
public static class DoctorReportRules
{
    /// <summary>Length of the default range in days — today and the 29 days before it.</summary>
    public const int DefaultRangeDays = 30;

    /// <summary>Longest range the report will ever build in one query — a little over a year.</summary>
    public const int MaxRangeDays = 366;

    /// <summary>
    /// The report's [From, To] range from the raw <c>from</c>/<c>to</c> query values. Either
    /// bound missing or unparsable falls back to the default range entirely — a report is either
    /// fully explicit or fully defaulted, never a mix of one typed bound and one guessed one,
    /// which would be a range the user never actually asked for. A backwards range (from after
    /// to) is swapped rather than rejected — a report link is exactly the kind of thing that gets
    /// pasted around and hand-edited. Whatever range results is then clamped to
    /// <see cref="MaxRangeDays"/> by pulling <c>From</c> forward; <c>To</c> never moves, since it
    /// is usually "today" or the visit date the report is being pulled for.
    /// </summary>
    public static (DateOnly From, DateOnly To) ResolveRange(string? fromRaw, string? toRaw, DateOnly today)
    {
        if (!AppTime.TryParseDay(fromRaw, out var from) || !AppTime.TryParseDay(toRaw, out var to))
        {
            return (today.AddDays(-(DefaultRangeDays - 1)), today);
        }

        if (from > to)
        {
            (from, to) = (to, from);
        }

        if (TotalDays(from, to) > MaxRangeDays)
        {
            from = to.AddDays(-(MaxRangeDays - 1));
        }

        return (from, to);
    }

    /// <summary>Inclusive day count of a [From, To] range — 1 when they are the same day.</summary>
    public static int TotalDays(DateOnly from, DateOnly to) => to.DayNumber - from.DayNumber + 1;

    /// <summary>
    /// Per-type entry totals for the range, in the app's standard type display order (built-ins
    /// in seed order, then user-added types alphabetically — see
    /// <see cref="EntryTypeRules.SortForDisplay{T}"/>) rather than by count, so the summary line
    /// reads in the same order every other type list in the app does. A type with nothing logged
    /// in the range is simply absent — there is no zero row to skip past.
    /// </summary>
    public static IReadOnlyList<DoctorTypeCount> TypeCounts(IEnumerable<string> entryTypes)
    {
        var counts = entryTypes
            .GroupBy(type => type, StringComparer.Ordinal)
            .Select(group => new DoctorTypeCount(group.Key, group.Count()));

        return EntryTypeRules.SortForDisplay(counts, count => count.Type);
    }

    /// <summary>
    /// How many distinct days in [From, To] carry an anxiety vote. Days outside the range
    /// contribute nothing, so a caller that over-fetches cannot bend the count — the same
    /// guarantee <see cref="AnxietyRules.BuildMonth"/> gives the anxiety report.
    /// </summary>
    public static int VotedDayCount(IEnumerable<DateOnly> voteDays, DateOnly from, DateOnly to) =>
        voteDays.Count(day => day >= from && day <= to);
}
