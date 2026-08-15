using MedHistory.Models;

namespace MedHistory.Services;

/// <summary>
/// One entry created by ticking a checklist slot, reduced to the fields that identify which
/// slot it ticked. Entries with no <see cref="AllocationId"/> are not checklist ticks and
/// never reach here.
/// </summary>
public readonly record struct ChecklistTick(int EntryId, int? AllocationId, string? Slot);

/// <summary>One slot of a checklist row: what to label the control, and whether it is ticked.</summary>
/// <param name="Slot">The slot itself.</param>
/// <param name="Name">Canonical name — the slot segment of the tick and untick URLs.</param>
/// <param name="Label">How the slot reads on screen.</param>
/// <param name="IsTicked">Whether an entry is linked to this allocation and slot.</param>
public readonly record struct ChecklistSlotState(MedSlots Slot, string Name, string Label, bool IsTicked);

/// <summary>
/// A checklist row as the day view renders it. Progress is not stored: a slot is ticked
/// exactly when an entry linked to it exists, so the row is rebuilt from the day's entries
/// every render and cannot drift out of step with them.
/// </summary>
public readonly record struct ChecklistRow(
    int AllocationId,
    string Name,
    string Description,
    IReadOnlyList<ChecklistSlotState> Slots)
{
    /// <summary>Doses expected that day — one per slot.</summary>
    public int RequiredCount => Slots.Count;

    public int DoneCount => Slots.Count(slot => slot.IsTicked);

    /// <summary>
    /// A row with no slots is never complete: it has nothing to tick, so calling it done would
    /// strike it through the moment it appeared. Validation stops such a row being created;
    /// this only decides what happens if one exists anyway.
    /// </summary>
    public bool IsComplete => Slots.Count > 0 && DoneCount == Slots.Count;
}

/// <summary>
/// Pure checklist rules — no clock, no database, no HTTP. The day view and the checklist POST
/// actions make every decision through here so they can be unit tested without a database.
///
/// Ticks are identified by the allocation and slot an entry is linked to, never by matching
/// the medication name. A Pill entry the user typed in by hand therefore does not tick
/// anything off: the checklist tracks the plan the user is working through, and only the
/// tick controls speak for it. Names are still compared case-insensitively where the plan
/// itself is concerned — adding a medication, and copying a day forward — because there the
/// name is free text the user types and "Eyedrop L" must not become a second row alongside
/// "eyedrop L".
/// </summary>
public static class ChecklistRules
{
    public const int NameMaxLength = MedAllocation.NameMaxLength;

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
    /// Returns one message per broken rule; an empty list means the allocation may be added.
    /// </summary>
    public static IReadOnlyList<string> ValidateNewAllocation(
        string? rawName,
        MedSlots slots,
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

        // Slots are the doses. None means a row that can never be worked through, so it is
        // rejected rather than stored — this is the rule that replaced a minimum dose count.
        if (MedPlanRules.SlotCount(slots) == 0)
        {
            errors.Add("Pick at least one time of day.");
        }

        return errors;
    }

    /// <summary>
    /// Builds one row per allocation, in the order given, each with one state per slot.
    /// Output order matches the input, and every allocation gets a row — an untouched
    /// medication is a row of empty ticks, not a missing one.
    ///
    /// Ticks that match no allocation are ignored, which is what makes a dangling link
    /// harmless: an entry whose allocation was deleted stays in the timeline as an ordinary
    /// Pill entry and ticks nothing. Ticks whose slot is missing or unrecognised are ignored
    /// the same way.
    /// </summary>
    public static IReadOnlyList<ChecklistRow> DeriveRows(
        IEnumerable<MedAllocation> allocations,
        IEnumerable<ChecklistTick> ticks)
    {
        var logged = ticks.ToList();

        return allocations
            .Select(allocation => new ChecklistRow(
                allocation.Id,
                allocation.Name,
                MedPlanRules.DescribeAllocation(allocation.MealRelation, allocation.Method),
                MedPlanRules.Each(allocation.Slots)
                    .Select(slot => new ChecklistSlotState(
                        slot,
                        MedPlanRules.SlotName(slot),
                        MedPlanRules.SlotLabel(slot),
                        FindTick(logged, allocation.Id, slot) is not null))
                    .ToList()))
            .ToList();
    }

    /// <summary>
    /// The entry that ticked one slot of one allocation, or null when the slot is not ticked —
    /// which is both what draws the control and what an untick deletes.
    ///
    /// Ticking is a no-op when a slot is already ticked, so a slot should only ever have one
    /// entry. If two exist anyway, the one inserted last wins: every tick of a day lands at the
    /// same instant for a past day, so the entry id is the only ordering that means anything,
    /// and for today it rises with the clock regardless.
    /// </summary>
    public static ChecklistTick? FindTick(IEnumerable<ChecklistTick> ticks, int allocationId, MedSlots slot)
    {
        if (slot == MedSlots.None)
        {
            return null;
        }

        var matches = ticks
            .Where(tick => tick.AllocationId == allocationId && SlotsMatch(tick.Slot, slot))
            .OrderByDescending(tick => tick.EntryId)
            .ToList();

        return matches.Count == 0 ? null : matches[0];
    }

    /// <summary>
    /// The previous day's allocations worth copying forward: those whose name is not already on
    /// the target day. Ticks are never part of this — only the plan is copied, so the new day
    /// starts with nothing ticked. Names repeated within the source are copied once.
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
    /// When a tick is logged. Ticking today records the actual moment; ticking a past or future
    /// day has no meaningful moment, so it lands at noon local — far enough from either midnight
    /// that it stays inside the day it was ticked for whatever the offset. The offset is passed
    /// in rather than read from the machine so this stays pure; the returned instant is always
    /// UTC, which is what Npgsql accepts for <c>timestamp with time zone</c>.
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

    private static bool SlotsMatch(string? storedSlot, MedSlots slot) =>
        MedPlanRules.TryParseSlot(storedSlot, out var parsed) && parsed == slot;
}
