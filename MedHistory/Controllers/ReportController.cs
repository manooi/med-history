using MedHistory.Data;
using MedHistory.Models;
using MedHistory.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Controllers;

/// <summary>
/// The month calendar of medication adherence — every medication at once, one cell per day.
/// Read-only: nothing here changes a plan or a dose, and every cell links back to the day page
/// where both are edited.
///
/// The month is two reads whatever it holds: the days' allocations, then the entries linked to
/// them. Which cell a tick lands in is the allocation's day, not the entry's timestamp — see
/// <see cref="ReportRules"/>, which owns that decision along with everything else the page shows.
/// </summary>
public class ReportController : Controller
{
    private readonly AppDbContext _db;

    public ReportController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("/med-report")]
    public Task<IActionResult> Index() => ShowMonth(ReportRules.FirstOfMonth(AppTime.Today()));

    [HttpGet("/med-report/{ym}")]
    public Task<IActionResult> ByMonth(string ym)
    {
        if (!ReportRules.TryParseMonth(ym, out var month))
        {
            // A hand-typed month is the only way to get here with garbage, and the current month
            // is a better answer than an error page — same as the day page's bad date.
            return Task.FromResult<IActionResult>(RedirectToAction(nameof(Index)));
        }

        return ShowMonth(month);
    }

    private async Task<IActionResult> ShowMonth(DateOnly anyDayOfMonth)
    {
        // Normalised here so the query range is the calendar month whatever the caller held.
        var month = ReportRules.FirstOfMonth(anyDayOfMonth);
        var next = month.AddMonths(1);

        // Projected: the report counts slots, so the name, dose and stock link are never read.
        var rows = await _db.MedAllocations
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
            : await LoadTicksAsync(allocationIds);

        var model = new ReportViewModel
        {
            Month = ReportRules.BuildMonth(month, allocations, ticks),
            Today = AppTime.Today()
        };

        return View("Index", model);
    }

    /// <summary>
    /// Every entry linked to one of the month's allocations. Deliberately not filtered by
    /// <c>OccurredAt</c>: a tick belongs to the day it was ticked for, and a retro tick's entry
    /// may sit on another date entirely.
    /// </summary>
    private async Task<List<ChecklistTick>> LoadTicksAsync(List<int> allocationIds)
    {
        var rows = await _db.Entries
            .AsNoTracking()
            .Where(e => e.ChecklistAllocationId != null && allocationIds.Contains(e.ChecklistAllocationId.Value))
            .Select(e => new { e.Id, e.ChecklistAllocationId, e.ChecklistSlot })
            .ToListAsync();

        return rows.Select(r => new ChecklistTick(r.Id, r.ChecklistAllocationId, r.ChecklistSlot)).ToList();
    }
}
