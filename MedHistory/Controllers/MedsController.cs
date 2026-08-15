using MedHistory.Data;
using MedHistory.Models;
using MedHistory.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Controllers;

/// <summary>
/// Maintains one day's medication plan — adding, removing and copying allocations forward
/// from the previous day. Ticking a dose off stays on the day view (see
/// <see cref="DayController"/>): this controller only ever changes what's planned, never a
/// logged dose.
/// </summary>
public class MedsController : Controller
{
    private readonly AppDbContext _db;
    private readonly ILogger<MedsController> _logger;

    public MedsController(AppDbContext db, ILogger<MedsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet("/day/{date}/meds")]
    public async Task<IActionResult> Index(string date)
    {
        if (!AppTime.TryParseDay(date, out var day))
        {
            return RedirectToAction(nameof(DayController.Index), "Day");
        }

        return View(await BuildModel(day));
    }

    [HttpPost("/day/{date}/meds")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddAllocation(string date, string? name, int requiredCount = ChecklistRules.MinRequiredCount)
    {
        if (!AppTime.TryParseDay(date, out var day))
        {
            return RedirectToAction(nameof(DayController.Index), "Day");
        }

        var existingNames = await AllocationNames(day);

        foreach (var error in ChecklistRules.ValidateNewAllocation(name, requiredCount, existingNames))
        {
            ModelState.AddModelError(string.Empty, error);
        }

        if (!ModelState.IsValid)
        {
            return View("Index", await BuildModel(day, name, requiredCount));
        }

        // Non-null: ValidateNewAllocation rejects a name that normalises away.
        var allocation = new MedAllocation
        {
            Day = day,
            Name = ChecklistRules.NormalizeName(name)!,
            RequiredCount = requiredCount
        };

        _db.MedAllocations.Add(allocation);
        await _db.SaveChangesAsync();

        // Ids and counts only — a medication name is health data and stays out of the log,
        // the same way entry notes do.
        _logger.LogInformation(
            "Allocation {AllocationId} added for {Day}, {RequiredCount} per day",
            allocation.Id, AppTime.Key(day), allocation.RequiredCount);

        return RedirectToDay(day);
    }

    [HttpPost("/day/{date}/meds/copy-previous")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CopyPreviousDay(string date)
    {
        if (!AppTime.TryParseDay(date, out var day))
        {
            return RedirectToAction(nameof(DayController.Index), "Day");
        }

        var previous = day.AddDays(-1);
        var source = await _db.MedAllocations.AsNoTracking().Where(a => a.Day == previous).OrderBy(a => a.Id).ToListAsync();
        var copied = ChecklistRules.AllocationsToCopy(source, await AllocationNames(day));

        // Allocations only: the copies start at 0/N however much of the previous day was ticked.
        foreach (var allocation in copied)
        {
            _db.MedAllocations.Add(new MedAllocation
            {
                Day = day,
                Name = allocation.Name,
                RequiredCount = allocation.RequiredCount
            });
        }

        if (copied.Count > 0)
        {
            await _db.SaveChangesAsync();
        }

        _logger.LogInformation("Copied {Count} allocation(s) from {Previous} to {Day}",
            copied.Count, AppTime.Key(previous), AppTime.Key(day));

        return RedirectToDay(day);
    }

    [HttpPost("/checklist/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveAllocation(int id)
    {
        var allocation = await _db.MedAllocations.FindAsync(id);

        if (allocation is null)
        {
            return NotFound();
        }

        // The row only. Pill entries logged against this medication are the day's record of
        // what was taken and are never touched by removing the plan for it.
        var day = allocation.Day;
        _db.MedAllocations.Remove(allocation);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Allocation {AllocationId} removed from {Day}", id, AppTime.Key(day));

        return RedirectToDay(day);
    }

    private async Task<MedsViewModel> BuildModel(DateOnly day, string? newMedName = null, int? newMedRequiredCount = null)
    {
        var allocations = await _db.MedAllocations
            .AsNoTracking()
            .Where(a => a.Day == day)
            .OrderBy(a => a.Id)
            .ToListAsync();

        return new MedsViewModel
        {
            Day = day,
            Allocations = allocations.Select(a => new MedAllocationRow
            {
                Id = a.Id,
                Name = a.Name,
                RequiredCount = a.RequiredCount
            }).ToList(),
            CanCopyPreviousDay = await _db.MedAllocations.AnyAsync(a => a.Day == day.AddDays(-1)),
            NewMedName = newMedName,
            NewMedRequiredCount = newMedRequiredCount ?? ChecklistRules.MinRequiredCount
        };
    }

    private async Task<List<string>> AllocationNames(DateOnly day) =>
        await _db.MedAllocations.AsNoTracking().Where(a => a.Day == day).Select(a => a.Name).ToListAsync();

    private IActionResult RedirectToDay(DateOnly day) =>
        RedirectToAction(nameof(Index), new { date = AppTime.Key(day) });
}
