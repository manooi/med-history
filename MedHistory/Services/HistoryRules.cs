using MedHistory.Models;

namespace MedHistory.Services;

/// <summary>
/// Pure history rules — no clock, no database, no HTTP. Controllers call these so
/// the same decisions can be unit tested without spinning up the app.
/// </summary>
public static class HistoryRules
{
    /// <summary>
    /// Groups entries by local calendar day and counts occurrences per type.
    /// Days are ordered newest first; within a day, types keep no particular order.
    /// </summary>
    public static IReadOnlyList<(DateOnly Day, IReadOnlyDictionary<EntryType, int> Counts)> GroupByDay(
        IEnumerable<(DateTimeOffset OccurredAt, EntryType Type)> entries,
        TimeSpan offset)
    {
        return entries
            .GroupBy(e => DateOnly.FromDateTime(e.OccurredAt.ToOffset(offset).DateTime))
            .Select(g => (
                Day: g.Key,
                Counts: (IReadOnlyDictionary<EntryType, int>)g
                    .GroupBy(e => e.Type)
                    .ToDictionary(t => t.Key, t => t.Count())))
            .OrderByDescending(d => d.Day)
            .ToList();
    }
}
