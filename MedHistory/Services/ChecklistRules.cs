using MedHistory.Models;

namespace MedHistory.Services;

/// <summary>
/// One logged Pill entry of a day, reduced to the fields the checklist reasons about.
/// </summary>
public readonly record struct PillLog(int EntryId, string? PillName, DateTimeOffset OccurredAt);

/// <summary>
/// Derived state of one checklist row. <see cref="DoneCount"/> is counted from the day's
/// Pill entries every time the day is rendered — it is never stored, so a Pill entry the
/// user typed in by hand counts towards the allocation just like a tick does.
/// </summary>
public readonly record struct ChecklistProgress(int AllocationId, string Name, int RequiredCount, int DoneCount)
{
    /// <summary>
    /// The count as shown, capped at <see cref="RequiredCount"/>: once the day's doses are
    /// in, the row reads N/N however many extra Pill entries exist. Nothing is deleted to
    /// make that true — over-counting is a display decision only, so the timeline stays a
    /// faithful record of what was actually taken.
    /// </summary>
    public int DisplayCount => Math.Min(DoneCount, RequiredCount);

    public bool IsComplete => DoneCount >= RequiredCount;

    /// <summary>Unticking needs something to remove.</summary>
    public bool CanUntick => DoneCount > 0;
}

/// <summary>
/// Pure checklist rules — no clock, no database, no HTTP. The day view and the checklist
/// POST actions make every decision through here so they can be unit tested without a
/// database.
///
/// Medication names are compared case-insensitively throughout. They are free text the
/// user types twice — once when allocating, again on any Pill entry created by hand — so
/// an ordinal match would silently split "Eyedrop L" and "eyedrop L" into two medications
/// that each show 0 done. The allocation's stored casing is the canonical one: a tick
/// always writes that, so ticked entries stay visually consistent in the timeline.
/// </summary>
public static class ChecklistRules
{
    public const int NameMaxLength = MedAllocation.NameMaxLength;

    public const int MinRequiredCount = 1;

    /// <summary>Upper bound so a mistyped count cannot render an absurd row.</summary>
    public const int MaxRequiredCount = 99;

    /// <summary>
    /// Trims surrounding whitespace; returns null when nothing is left. Stored names are
    /// always this normalised form.
    /// </summary>
    public static string? NormalizeName(string? raw)
    {
        var trimmed = raw?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    public static bool NamesMatch(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a)
        && !string.IsNullOrWhiteSpace(b)
        && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether an entry counts towards checklist progress: the built-in Pill type, exactly.
    /// Deliberately not <see cref="EntryRules.RequiresPillName"/> — that answers the broader
    /// "does this type carry a pill name", which is Pill alone today but is free to grow. A
    /// tick creates a built-in Pill entry, so the count has to ask the narrower question, or
    /// it would start including entries no tick could ever have produced.
    ///
    /// Ordinal, matching the SQL twin of this check in <c>DayController.PillLogs</c>: entry
    /// types are stored in their canonical casing and the database comparison is
    /// case-sensitive, so a looser match here would make the two paths disagree.
    /// </summary>
    public static bool IsPillEntry(string? type) =>
        string.Equals(type, BuiltInEntryTypes.Pill, StringComparison.Ordinal);

    /// <summary>
    /// Returns one message per broken rule; an empty list means the allocation may be added.
    /// </summary>
    public static IReadOnlyList<string> ValidateNewAllocation(
        string? rawName,
        int requiredCount,
        IEnumerable<string> namesAlreadyOnDay)
    {
        var name = NormalizeName(rawName);

        if (name is null)
        {
            return ["Medication name is required."];
        }

        var errors = new List<string>();

        if (name.Length > NameMaxLength)
        {
            errors.Add($"Medication name must be {NameMaxLength} characters or fewer.");
        }

        if (namesAlreadyOnDay.Any(existing => NamesMatch(existing, name)))
        {
            errors.Add($"\"{name}\" is already on this day's checklist.");
        }

        if (requiredCount < MinRequiredCount)
        {
            errors.Add($"Times per day must be at least {MinRequiredCount}.");
        }
        else if (requiredCount > MaxRequiredCount)
        {
            errors.Add($"Times per day must be {MaxRequiredCount} or fewer.");
        }

        return errors;
    }

    /// <summary>
    /// Counts each allocation's Pill entries for the day. Output order matches the input
    /// allocations, and every allocation gets a row — an untouched medication is a 0/N row,
    /// not a missing one.
    /// </summary>
    public static IReadOnlyList<ChecklistProgress> DeriveProgress(
        IEnumerable<MedAllocation> allocations,
        IEnumerable<PillLog> pillEntries)
    {
        var pills = pillEntries.ToList();

        return allocations
            .Select(allocation => new ChecklistProgress(
                allocation.Id,
                allocation.Name,
                allocation.RequiredCount,
                pills.Count(pill => NamesMatch(pill.PillName, allocation.Name))))
            .ToList();
    }

    /// <summary>
    /// The previous day's allocations worth copying forward: those whose name is not already
    /// on the target day. Ticks are never part of this — only the allocation rows are copied,
    /// so the new day starts at 0/N. Names repeated within the source are copied once.
    /// </summary>
    public static IReadOnlyList<MedAllocation> AllocationsToCopy(
        IEnumerable<MedAllocation> previousDay,
        IEnumerable<string> namesAlreadyOnDay)
    {
        var taken = namesAlreadyOnDay.ToList();
        var copied = new List<MedAllocation>();

        foreach (var allocation in previousDay)
        {
            if (taken.Any(name => NamesMatch(name, allocation.Name)))
            {
                continue;
            }

            copied.Add(allocation);
            taken.Add(allocation.Name);
        }

        return copied;
    }

    /// <summary>
    /// When a tick is logged. Ticking today records the actual moment; ticking a past or
    /// future day has no meaningful moment, so it lands at noon local — far enough from
    /// either midnight that it stays inside the day it was ticked for whatever the offset.
    /// The offset is passed in rather than read from the machine so this stays pure; the
    /// returned instant is always UTC, which is what Npgsql accepts for
    /// <c>timestamp with time zone</c>.
    /// </summary>
    public static DateTimeOffset TickTime(DateOnly day, DateOnly today, DateTimeOffset nowUtc, TimeSpan dayOffset)
    {
        if (day == today)
        {
            return nowUtc.ToUniversalTime();
        }

        var noon = new DateTimeOffset(day.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Unspecified), dayOffset);
        return noon.ToUniversalTime();
    }

    /// <summary>
    /// The entry an untick removes: the latest matching Pill entry of the day. Entries
    /// sharing the newest timestamp — which ticking twice within the same second can
    /// produce — are broken by the highest entry id, i.e. the row inserted last, so an
    /// untick always undoes the most recent tick.
    /// Null when nothing matches, which the caller treats as a no-op.
    /// </summary>
    public static PillLog? NewestMatch(IEnumerable<PillLog> pillEntries, string allocationName)
    {
        var matches = pillEntries
            .Where(pill => NamesMatch(pill.PillName, allocationName))
            .OrderByDescending(pill => pill.OccurredAt)
            .ThenByDescending(pill => pill.EntryId)
            .ToList();

        return matches.Count == 0 ? null : matches[0];
    }
}
