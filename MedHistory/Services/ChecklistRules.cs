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
/// every render and cannot drift out of step with them. The same holds for the stock left —
/// it is summed from every logged dose, not carried anywhere.
/// </summary>
/// <param name="DoseQuantity">Units each slot is worth, as the plan stands now.</param>
/// <param name="StockRemaining">
/// Units left of the stock this medication draws on, or null when nothing stocks it.
/// </param>
public readonly record struct ChecklistRow(
    int AllocationId,
    string Name,
    string Description,
    decimal DoseQuantity,
    decimal? StockRemaining,
    IReadOnlyList<ChecklistSlotState> Slots)
{
    /// <summary>Doses expected that day — one per slot.</summary>
    public int RequiredCount => Slots.Count;

    /// <summary>"×2" when a slot is worth more or less than one unit, empty otherwise.</summary>
    public string QuantityLabel => MedPlanRules.QuantityLabel(DoseQuantity);

    /// <summary>"(18 left)" when this medication is stocked, empty otherwise.</summary>
    public string StockLabel => MedStockRules.RemainingLabel(StockRemaining);

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
/// the medication name. A Med entry the user typed in by hand therefore does not tick
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
    /// Longest bulk-add range, inclusive of both ends. A guard against a typo'd year, not a
    /// meaningful ceiling — a year plus a day is generous for "add this to every day for a
    /// while".
    /// </summary>
    public const int MaxRangeDays = 366;

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
    /// Returns one message per broken rule for a dose quantity, and the parsed value when there
    /// are none. Split out of <see cref="ValidateNewAllocation"/> because the quantity is one
    /// field the add form, the range add and the edit form all post and all judge identically.
    ///
    /// The raw string is taken rather than a decimal so that "not a number at all" is a rule
    /// broken here with a readable message, rather than a model-binding failure phrased in
    /// framework language.
    /// </summary>
    public static IReadOnlyList<string> ValidateDoseQuantity(string? rawQuantity, out decimal quantity)
    {
        quantity = MedPlanRules.DefaultDoseQuantity;

        if (string.IsNullOrWhiteSpace(rawQuantity))
        {
            return ["Dose is required."];
        }

        if (!MedPlanRules.TryParseQuantity(rawQuantity, out var parsed))
        {
            return ["Dose must be a number."];
        }

        if (parsed < MedPlanRules.MinDoseQuantity || parsed > MedPlanRules.MaxDoseQuantity)
        {
            return
            [
                $"Dose must be between {MedPlanRules.FormatQuantity(MedPlanRules.MinDoseQuantity)} " +
                $"and {MedPlanRules.FormatQuantity(MedPlanRules.MaxDoseQuantity)}."
            ];
        }

        // Only worth saying once the value is in range — "between 0.25 and 99" already covers
        // what is wrong with 0.1, and two messages about one field read as two problems.
        if (parsed % MedPlanRules.DoseQuantityStep != 0m)
        {
            return [$"Dose must be a multiple of {MedPlanRules.FormatQuantity(MedPlanRules.DoseQuantityStep)}."];
        }

        quantity = parsed;

        return [];
    }

    /// <summary>
    /// Returns one message per broken rule for a bulk-add date range; an empty list means the
    /// range may be expanded. Name and slot rules are still <see cref="ValidateNewAllocation"/>'s
    /// job — this only judges the range itself. A day that already holds the medication is not
    /// a validation error either: <see cref="DaysToAllocate"/> skips it instead of rejecting the
    /// whole range.
    /// </summary>
    public static IReadOnlyList<string> ValidateRange(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            return ["End date must be on or after the start date."];
        }

        return RangeLength(from, to) > MaxRangeDays
            ? [$"Range too long (max {MaxRangeDays} days)."]
            : [];
    }

    /// <summary>Days spanned by [from, to], inclusive of both ends.</summary>
    public static int RangeLength(DateOnly from, DateOnly to) => to.DayNumber - from.DayNumber + 1;

    /// <summary>
    /// Every calendar day in [from, to], inclusive, in day order. Callers validate the range
    /// first via <see cref="ValidateRange"/>; given <paramref name="to"/> before
    /// <paramref name="from"/> this returns empty rather than looping backwards.
    /// </summary>
    public static IReadOnlyList<DateOnly> ExpandRange(DateOnly from, DateOnly to)
    {
        var days = new List<DateOnly>();

        for (var day = from; day <= to; day = day.AddDays(1))
        {
            days.Add(day);
        }

        return days;
    }

    /// <summary>
    /// The days from <paramref name="days"/> that do not already hold an allocation with this
    /// name — matched the same way <see cref="NamesMatch"/> matches everywhere else a
    /// medication name is compared. A day absent from <paramref name="existingNamesByDay"/> is
    /// treated as holding nothing yet. When every day is already taken this returns empty,
    /// which the caller treats as "nothing to add", not an error.
    /// </summary>
    public static IReadOnlyList<DateOnly> DaysToAllocate(
        IEnumerable<DateOnly> days,
        string name,
        IReadOnlyDictionary<DateOnly, IReadOnlyList<string>> existingNamesByDay) =>
        days
            .Where(day => !existingNamesByDay.TryGetValue(day, out var names)
                || !names.Any(existing => NamesMatch(existing, name)))
            .ToList();

    /// <summary>
    /// Builds one row per allocation, in the order given, each with one state per slot.
    /// Output order matches the input, and every allocation gets a row — an untouched
    /// medication is a row of empty ticks, not a missing one.
    ///
    /// Ticks that match no allocation are ignored, which is what makes a dangling link
    /// harmless: an entry whose allocation was deleted stays in the timeline as an ordinary
    /// Med entry and ticks nothing. Ticks whose slot is missing or unrecognised are ignored
    /// the same way.
    ///
    /// <paramref name="stock"/> is optional because the checklist works perfectly well with
    /// nothing stocked: a medication no stock row names simply shows no count, and passing
    /// nothing at all is that case for every row. Which stock a row reads is the allocation's own
    /// link where it has one, falling back to its name where it does not — the same order the
    /// doses beneath it are counted in, so the row never shows a count the meds page disagrees
    /// with. See <see cref="MedStockRules.FindRemaining"/>.
    /// </summary>
    public static IReadOnlyList<ChecklistRow> DeriveRows(
        IEnumerable<MedAllocation> allocations,
        IEnumerable<ChecklistTick> ticks,
        IEnumerable<MedStockRow>? stock = null)
    {
        var logged = ticks.ToList();
        var stocked = stock?.ToList();

        return allocations
            .Select(allocation => new ChecklistRow(
                allocation.Id,
                allocation.Name,
                MedPlanRules.DescribeAllocation(allocation.MealRelation, allocation.Method),
                allocation.DoseQuantity,
                MedStockRules.FindRemaining(stocked, allocation.MedStockId, allocation.Name),
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
    /// An allocation reduced to what edit selection and rename-collision checks need: which row,
    /// which day, and what name it currently carries.
    /// </summary>
    public readonly record struct AllocationRef(int Id, DateOnly Day, string Name);

    /// <summary>
    /// The allocations one edit applies to. Without <paramref name="applyForward"/> this is
    /// always just <paramref name="edited"/> — a same-day edit never reaches past its own row.
    /// With it set, every allocation in <paramref name="candidates"/> dated on or after the
    /// edited row's day that shares its (pre-edit) name is included too: a day at or after this
    /// one with a different-named medication, or a day before this one regardless of name, is
    /// never touched.
    /// </summary>
    public static IReadOnlyList<AllocationRef> AffectedAllocations(
        AllocationRef edited, bool applyForward, IEnumerable<AllocationRef> candidates) =>
        applyForward
            ? candidates.Where(a => a.Day >= edited.Day && NamesMatch(a.Name, edited.Name)).ToList()
            : [edited];

    /// <summary>
    /// The days, among the keys of <paramref name="namesByDay"/>, that already hold some other
    /// allocation named <paramref name="newName"/> — saving the edit would create a second row
    /// with that name on that day. "Other" excludes the allocations being edited themselves,
    /// identified by <paramref name="excludingIds"/>, so an edit that leaves the name unchanged
    /// never collides with its own row(s).
    /// </summary>
    public static IReadOnlyList<DateOnly> RenameCollisionDays(
        string newName,
        IReadOnlySet<int> excludingIds,
        IReadOnlyDictionary<DateOnly, IReadOnlyList<AllocationRef>> namesByDay) =>
        namesByDay
            .Where(kv => kv.Value.Any(a => !excludingIds.Contains(a.Id) && NamesMatch(a.Name, newName)))
            .Select(kv => kv.Key)
            .OrderBy(day => day)
            .ToList();

    /// <summary>
    /// Caps how many day labels a rename-collision message names by hand before summarising the
    /// rest, so a forward-apply that collides on many days does not produce an unreadable error.
    /// </summary>
    public const int MaxCollisionDaysListed = 3;

    /// <summary>
    /// Joins day labels for a validation message, capping at <see cref="MaxCollisionDaysListed"/>
    /// and summarising anything past that as "and N more" rather than naming every day.
    /// </summary>
    public static string JoinDayLabels(IReadOnlyList<string> labels) =>
        labels.Count <= MaxCollisionDaysListed
            ? string.Join(", ", labels)
            : $"{string.Join(", ", labels.Take(MaxCollisionDaysListed))}, and {labels.Count - MaxCollisionDaysListed} more";

    /// <summary>
    /// When a tick is logged. Ticking today records the actual moment; ticking a past or future
    /// day has no meaningful moment, so it lands at the ticked slot's representative clock time
    /// (see <see cref="MedPlanRules.SlotTime"/>) on that day — far enough from either midnight
    /// that it stays inside the day it was ticked for whatever the offset. The offset is passed
    /// in rather than read from the machine so this stays pure; the returned instant is always
    /// UTC, which is what Npgsql accepts for <c>timestamp with time zone</c>.
    /// </summary>
    public static DateTimeOffset TickTime(
        DateOnly day, DateOnly today, DateTimeOffset nowUtc, TimeSpan dayOffset, MedSlots slot)
    {
        if (day == today)
        {
            return nowUtc.ToUniversalTime();
        }

        var slotTime = new DateTimeOffset(
            day.ToDateTime(MedPlanRules.SlotTime(slot), DateTimeKind.Unspecified), dayOffset);
        return slotTime.ToUniversalTime();
    }

    private static bool SlotsMatch(string? storedSlot, MedSlots slot) =>
        MedPlanRules.TryParseSlot(storedSlot, out var parsed) && parsed == slot;
}
