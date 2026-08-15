using MedHistory.Data;
using MedHistory.Models;
using MedHistory.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Controllers;

/// <summary>
/// Maintains one day's medication plan — adding, removing and copying allocations forward
/// from the previous day. Ticking a slot off stays on the day view (see
/// <see cref="DayController"/>): this controller only ever changes what's planned, never a
/// logged dose.
///
/// There is no edit action. A plan is four small fields, so changing one is removing the row
/// and adding it again — which the delete already makes safe, since the doses logged against
/// an allocation outlive it.
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
    public async Task<IActionResult> AddAllocation(
        string date,
        string? name,
        string[]? slots,
        MealRelation mealRelation = MealRelation.None,
        MedMethod method = MedMethod.Eat)
    {
        if (!AppTime.TryParseDay(date, out var day))
        {
            return RedirectToAction(nameof(DayController.Index), "Day");
        }

        // The checkboxes post slot names; names that are not slots are dropped, which
        // ValidateNewAllocation then sees as the empty set it rejects.
        var chosen = MedPlanRules.ParseSlots(slots ?? []);
        var existingNames = await AllocationNames(day);

        foreach (var error in ChecklistRules.ValidateNewAllocation(name, chosen, existingNames))
        {
            ModelState.AddModelError(string.Empty, error);
        }

        if (!ModelState.IsValid)
        {
            return View("Index", await BuildModel(day, name, chosen, mealRelation, method));
        }

        // Non-null: ValidateNewAllocation rejects a name that normalises away.
        var allocation = new MedAllocation
        {
            Day = day,
            Name = ChecklistRules.NormalizeName(name)!,
            Slots = chosen,
            MealRelation = mealRelation,
            Method = method
        };

        _db.MedAllocations.Add(allocation);
        await _db.SaveChangesAsync();

        // Ids and structure only — a medication name is health data and stays out of the log,
        // the same way entry notes do.
        _logger.LogInformation(
            "Allocation {AllocationId} added for {Day}, slots {Slots}",
            allocation.Id, AppTime.Key(day), MedPlanRules.FormatSlots(allocation.Slots));

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

        // The plan only, in full: the copies carry the same slots, meal relation and method, and
        // start with nothing ticked however much of the previous day was.
        foreach (var allocation in copied)
        {
            _db.MedAllocations.Add(new MedAllocation
            {
                Day = day,
                Name = allocation.Name,
                Slots = allocation.Slots,
                MealRelation = allocation.MealRelation,
                Method = allocation.Method
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

    private async Task<MedsViewModel> BuildModel(
        DateOnly day,
        string? newMedName = null,
        MedSlots newMedSlots = MedSlots.None,
        MealRelation newMedMealRelation = MealRelation.None,
        MedMethod newMedMethod = MedMethod.Eat)
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
                SlotLabels = MedPlanRules.Each(a.Slots).Select(MedPlanRules.SlotLabel).ToList(),
                Description = MedPlanRules.DescribeAllocation(a.MealRelation, a.Method)
            }).ToList(),
            CanCopyPreviousDay = await _db.MedAllocations.AnyAsync(a => a.Day == day.AddDays(-1)),
            NewMedName = newMedName,
            NewMedSlots = newMedSlots,
            NewMedMealRelation = newMedMealRelation,
            NewMedMethod = newMedMethod
        };
    }

    private async Task<List<string>> AllocationNames(DateOnly day) =>
        await _db.MedAllocations.AsNoTracking().Where(a => a.Day == day).Select(a => a.Name).ToListAsync();

    private IActionResult RedirectToDay(DateOnly day) =>
        RedirectToAction(nameof(Index), new { date = AppTime.Key(day) });
}
