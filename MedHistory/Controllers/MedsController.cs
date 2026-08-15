using MedHistory.Data;
using MedHistory.Models;
using MedHistory.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Controllers;

/// <summary>
/// Maintains one day's medication plan — adding, editing, removing and copying allocations
/// forward from the previous day. Ticking a slot off stays on the day view (see
/// <see cref="DayController"/>): this controller only ever changes what's planned, never a
/// logged dose.
///
/// Editing changes the plan only. Entries a tick already created keep whatever
/// <c>PillName</c>/<c>Note</c> they were logged with — a tick is a historical fact, and an edit
/// reaches forward, never back. An edit may also be applied to every future allocation that
/// shares the row's pre-edit name via <c>applyForward</c>, including a rename; see
/// <see cref="ChecklistRules.AffectedAllocations"/>.
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
        MedMethod method = MedMethod.Eat,
        string? from = null,
        string? to = null)
    {
        if (!AppTime.TryParseDay(date, out var day))
        {
            return RedirectToAction(nameof(DayController.Index), "Day");
        }

        // The range defaults to the page's own day — a single-day add is the degenerate case
        // of a one-day range, not a separate code path.
        if (!AppTime.TryParseDay(from, out var rangeFrom))
        {
            rangeFrom = day;
        }

        if (!AppTime.TryParseDay(to, out var rangeTo))
        {
            rangeTo = rangeFrom;
        }

        // The checkboxes post slot names; names that are not slots are dropped, which
        // ValidateNewAllocation then sees as the empty set it rejects.
        var chosen = MedPlanRules.ParseSlots(slots ?? []);

        // A day within the range that already holds this name is skipped, not rejected — so
        // no "already on this day" check here; ValidateNewAllocation still owns the name and
        // slot rules that apply regardless of which days end up allocated.
        foreach (var error in ChecklistRules.ValidateNewAllocation(name, chosen, []))
        {
            ModelState.AddModelError(string.Empty, error);
        }

        foreach (var error in ChecklistRules.ValidateRange(rangeFrom, rangeTo))
        {
            ModelState.AddModelError(string.Empty, error);
        }

        if (!ModelState.IsValid)
        {
            return View("Index", await BuildModel(day, name, chosen, mealRelation, method, rangeFrom, rangeTo));
        }

        // Non-null: ValidateNewAllocation rejects a name that normalises away.
        var normalizedName = ChecklistRules.NormalizeName(name)!;
        var candidateDays = ChecklistRules.ExpandRange(rangeFrom, rangeTo);
        var existingByDay = await AllocationNamesByDay(rangeFrom, rangeTo);
        var targetDays = ChecklistRules.DaysToAllocate(candidateDays, normalizedName, existingByDay);

        foreach (var target in targetDays)
        {
            _db.MedAllocations.Add(new MedAllocation
            {
                Day = target,
                Name = normalizedName,
                Slots = chosen,
                MealRelation = mealRelation,
                Method = method
            });
        }

        if (targetDays.Count > 0)
        {
            await _db.SaveChangesAsync();
        }

        // Ids and structure only — a medication name is health data and stays out of the log,
        // the same way entry notes do.
        _logger.LogInformation(
            "Allocation added for {Count} of {Candidates} day(s) in {From}..{To}, slots {Slots}",
            targetDays.Count, candidateDays.Count, AppTime.Key(rangeFrom), AppTime.Key(rangeTo),
            MedPlanRules.FormatSlots(chosen));

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

    [HttpGet("/checklist/{id:int}/edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var allocation = await _db.MedAllocations.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);

        if (allocation is null)
        {
            return NotFound();
        }

        return View(new MedAllocationEditViewModel
        {
            Id = allocation.Id,
            Day = allocation.Day,
            Name = allocation.Name,
            Slots = allocation.Slots,
            MealRelation = allocation.MealRelation,
            Method = allocation.Method,
            ApplyForward = false
        });
    }

    [HttpPost("/checklist/{id:int}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(
        int id,
        string? name,
        string[]? slots,
        MealRelation mealRelation = MealRelation.None,
        MedMethod method = MedMethod.Eat,
        bool applyForward = false)
    {
        var allocation = await _db.MedAllocations.FindAsync(id);

        if (allocation is null)
        {
            return NotFound();
        }

        var chosen = MedPlanRules.ParseSlots(slots ?? []);
        var day = allocation.Day;

        IActionResult Invalid() => View("Edit", new MedAllocationEditViewModel
        {
            Id = allocation.Id,
            Day = day,
            Name = name,
            Slots = chosen,
            MealRelation = mealRelation,
            Method = method,
            ApplyForward = applyForward
        });

        // Reuses the add form's name and slot rules. Its duplicate-name check is skipped here
        // (empty list) because "already on this day" means something different for an edit —
        // the rename-collision check below owns that, and excludes the row(s) being edited from
        // themselves so an unchanged name is never flagged.
        foreach (var error in ChecklistRules.ValidateNewAllocation(name, chosen, []))
        {
            ModelState.AddModelError(string.Empty, error);
        }

        if (!ModelState.IsValid)
        {
            return Invalid();
        }

        // Non-null: ValidateNewAllocation rejects a name that normalises away.
        var normalizedName = ChecklistRules.NormalizeName(name)!;
        var editedRef = new ChecklistRules.AllocationRef(allocation.Id, day, allocation.Name);

        IReadOnlyList<ChecklistRules.AllocationRef> candidates = applyForward
            ? await AllocationRefsFrom(day)
            : [editedRef];

        var affected = ChecklistRules.AffectedAllocations(editedRef, applyForward, candidates);
        var affectedIds = affected.Select(a => a.Id).ToHashSet();

        var namesByDay = await AllocationRefsByDay(affected.Select(a => a.Day).Distinct().ToList());
        var collisionDays = ChecklistRules.RenameCollisionDays(normalizedName, affectedIds, namesByDay);

        if (collisionDays.Count > 0)
        {
            var labels = collisionDays.Select(AppTime.DayLabel).ToList();
            ModelState.AddModelError(string.Empty,
                $"\"{normalizedName}\" is already used on {ChecklistRules.JoinDayLabels(labels)}.");

            return Invalid();
        }

        var rows = await _db.MedAllocations.Where(a => affectedIds.Contains(a.Id)).ToListAsync();

        foreach (var row in rows)
        {
            row.Name = normalizedName;
            row.Slots = chosen;
            row.MealRelation = mealRelation;
            row.Method = method;
        }

        await _db.SaveChangesAsync();

        // Ids and structure only, same reasoning as AddAllocation's log line.
        _logger.LogInformation(
            "Allocation {AllocationId} edited, {Count} row(s) affected, applyForward={ApplyForward}",
            id, rows.Count, applyForward);

        return RedirectToDay(day);
    }

    private async Task<MedsViewModel> BuildModel(
        DateOnly day,
        string? newMedName = null,
        MedSlots newMedSlots = MedSlots.None,
        MealRelation newMedMealRelation = MealRelation.None,
        MedMethod newMedMethod = MedMethod.Eat,
        DateOnly? newMedFrom = null,
        DateOnly? newMedTo = null)
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
            NewMedMethod = newMedMethod,
            // Un-set only when the GET view builds the form fresh; a rejected submit always
            // passes both through explicitly, echoing back exactly what was typed.
            NewMedFrom = newMedFrom ?? day,
            NewMedTo = newMedTo ?? day
        };
    }

    private async Task<List<string>> AllocationNames(DateOnly day) =>
        await _db.MedAllocations.AsNoTracking().Where(a => a.Day == day).Select(a => a.Name).ToListAsync();

    /// <summary>Every allocation name on each day in [from, to], inclusive — for range skip-checks.</summary>
    private async Task<IReadOnlyDictionary<DateOnly, IReadOnlyList<string>>> AllocationNamesByDay(
        DateOnly from, DateOnly to)
    {
        var rows = await _db.MedAllocations
            .AsNoTracking()
            .Where(a => a.Day >= from && a.Day <= to)
            .Select(a => new { a.Day, a.Name })
            .ToListAsync();

        return rows
            .GroupBy(r => r.Day)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(r => r.Name).ToList());
    }

    /// <summary>Every allocation dated on or after <paramref name="fromDay"/> — the applyForward candidate pool.</summary>
    private async Task<IReadOnlyList<ChecklistRules.AllocationRef>> AllocationRefsFrom(DateOnly fromDay) =>
        await _db.MedAllocations
            .AsNoTracking()
            .Where(a => a.Day >= fromDay)
            .Select(a => new ChecklistRules.AllocationRef(a.Id, a.Day, a.Name))
            .ToListAsync();

    /// <summary>Every allocation on each of the given days — for the rename-collision check.</summary>
    private async Task<IReadOnlyDictionary<DateOnly, IReadOnlyList<ChecklistRules.AllocationRef>>> AllocationRefsByDay(
        IReadOnlyCollection<DateOnly> days)
    {
        var rows = await _db.MedAllocations
            .AsNoTracking()
            .Where(a => days.Contains(a.Day))
            .Select(a => new ChecklistRules.AllocationRef(a.Id, a.Day, a.Name))
            .ToListAsync();

        return rows
            .GroupBy(r => r.Day)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ChecklistRules.AllocationRef>)g.ToList());
    }

    private IActionResult RedirectToDay(DateOnly day) =>
        RedirectToAction(nameof(Index), new { date = AppTime.Key(day) });
}
