using MedHistory.Models;
using MedHistory.Services;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Data;

/// <summary>
/// The database side of a day's med checklist: the allocation lookups and reads that
/// <see cref="MedHistory.Controllers.MedsController"/> uses to validate an add/edit against the
/// rest of the plan, and the tick/slot reads <see cref="MedHistory.Controllers.DayController"/>
/// uses to resolve and answer a checklist POST. Shared here rather than duplicated on either
/// controller so the query shapes — and the <c>AsNoTracking</c> choices that go with them — stay
/// single-sourced.
/// </summary>
public static class AllocationQueries
{
    public static async Task<List<string>> AllocationNamesAsync(this AppDbContext db, DateOnly day) =>
        await db.MedAllocations.AsNoTracking().Where(a => a.Day == day).Select(a => a.Name).ToListAsync();

    /// <summary>Every allocation name on each day in [from, to], inclusive — for range skip-checks.</summary>
    public static async Task<IReadOnlyDictionary<DateOnly, IReadOnlyList<string>>> AllocationNamesByDayAsync(
        this AppDbContext db, DateOnly from, DateOnly to)
    {
        var rows = await db.MedAllocations
            .AsNoTracking()
            .Where(a => a.Day >= from && a.Day <= to)
            .Select(a => new { a.Day, a.Name })
            .ToListAsync();

        return rows
            .GroupBy(r => r.Day)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(r => r.Name).ToList());
    }

    /// <summary>Every allocation dated on or after <paramref name="fromDay"/> — the applyForward candidate pool.</summary>
    public static async Task<IReadOnlyList<ChecklistRules.AllocationRef>> AllocationRefsFromAsync(
        this AppDbContext db, DateOnly fromDay) =>
        await db.MedAllocations
            .AsNoTracking()
            .Where(a => a.Day >= fromDay)
            .Select(a => new ChecklistRules.AllocationRef(a.Id, a.Day, a.Name))
            .ToListAsync();

    /// <summary>Every allocation on each of the given days — for the rename-collision check.</summary>
    public static async Task<IReadOnlyDictionary<DateOnly, IReadOnlyList<ChecklistRules.AllocationRef>>> AllocationRefsByDayAsync(
        this AppDbContext db, IReadOnlyCollection<DateOnly> days)
    {
        var rows = await db.MedAllocations
            .AsNoTracking()
            .Where(a => days.Contains(a.Day))
            .Select(a => new ChecklistRules.AllocationRef(a.Id, a.Day, a.Name))
            .ToListAsync();

        return rows
            .GroupBy(r => r.Day)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ChecklistRules.AllocationRef>)g.ToList());
    }

    /// <summary>
    /// Loads the allocation a checklist POST names, together with the slot it addresses. The
    /// allocation comes back null when either is unusable: no such allocation, an unrecognised
    /// slot name, or a slot the allocation does not have. Both are route values a hand-made
    /// request can say anything in, so the slot is checked against the plan and not trusted.
    /// </summary>
    public static async Task<(MedAllocation? Allocation, MedSlots Slot)> ResolveSlotAsync(
        this AppDbContext db, int id, string? slot)
    {
        var allocation = await db.MedAllocations.FindAsync(id);

        if (allocation is null || !MedPlanRules.TryParseSlot(slot, out var parsed) || !allocation.Slots.HasFlag(parsed))
        {
            return (null, MedSlots.None);
        }

        return (allocation, parsed);
    }

    /// <summary>
    /// The day's checklist ticks — the entries a slot control created, which is the whole of
    /// what the checklist reads. Scoped to the day rather than to the allocation so that an
    /// entry the user has since moved to another date stops counting here, exactly as it stops
    /// appearing in the day's timeline.
    /// </summary>
    public static async Task<List<ChecklistTick>> TicksAsync(this AppDbContext db, DateOnly day)
    {
        var (start, end) = AppTime.DayRange(day);

        var rows = await db.Entries
            .AsNoTracking()
            .Where(e => e.OccurredAt >= start && e.OccurredAt < end && e.ChecklistAllocationId != null)
            .Select(e => new { e.Id, e.ChecklistAllocationId, e.ChecklistSlot })
            .ToListAsync();

        return rows.Select(r => new ChecklistTick(r.Id, r.ChecklistAllocationId, r.ChecklistSlot)).ToList();
    }
}
