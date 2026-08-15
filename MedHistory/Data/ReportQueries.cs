using MedHistory.Models;
using MedHistory.Services;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Data;

/// <summary>
/// The database side of the med report's month calendar: the month's allocations, then the
/// entries linked to them. Lives here so <see cref="MedHistory.Controllers.ReportController"/>
/// stays route parsing and view selection only — see <see cref="ReportRules"/>, which owns
/// everything the page shows.
/// </summary>
public static class ReportQueries
{
    public static async Task<ReportViewModel> MedMonthAsync(this AppDbContext db, DateOnly anyDayOfMonth)
    {
        // Normalised here so the query range is the calendar month whatever the caller held.
        var month = ReportRules.FirstOfMonth(anyDayOfMonth);
        var next = month.AddMonths(1);

        // Projected: the report counts slots, so the name, dose and stock link are never read.
        var rows = await db.MedAllocations
            .AsNoTracking()
            .Where(a => a.Day >= month && a.Day < next)
            .Select(a => new { a.Id, a.Day, a.Slots })
            .ToListAsync();

        var allocations = rows.Select(r => new ReportAllocation(r.Id, r.Day, r.Slots)).ToList();
        var allocationIds = allocations.Select(a => a.Id).ToList();

        // One read for the whole month rather than one per day, and skipped outright when the
        // month has no plan at all — there is then nothing a tick could be linked to.
        IReadOnlyList<ChecklistTick> ticks = allocationIds.Count == 0
            ? []
            : await LoadTicksAsync(db, allocationIds);

        return new ReportViewModel
        {
            Month = ReportRules.BuildMonth(month, allocations, ticks),
            Today = AppTime.Today()
        };
    }

    /// <summary>
    /// Every entry linked to one of the month's allocations. Deliberately not filtered by
    /// <c>OccurredAt</c>: a tick belongs to the day it was ticked for, and a retro tick's entry
    /// may sit on another date entirely.
    /// </summary>
    private static async Task<List<ChecklistTick>> LoadTicksAsync(AppDbContext db, List<int> allocationIds)
    {
        var rows = await db.Entries
            .AsNoTracking()
            .Where(e => e.ChecklistAllocationId != null && allocationIds.Contains(e.ChecklistAllocationId.Value))
            .Select(e => new { e.Id, e.ChecklistAllocationId, e.ChecklistSlot })
            .ToListAsync();

        return rows.Select(r => new ChecklistTick(r.Id, r.ChecklistAllocationId, r.ChecklistSlot)).ToList();
    }
}
