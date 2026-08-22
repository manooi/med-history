using System.Globalization;
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
/// <c>PillName</c>, <c>Note</c>, <c>DoseQuantity</c> and <c>MedStockId</c> they were logged
/// with — a tick is a historical fact, and an edit reaches forward, never back. An edit may also
/// be applied to every future allocation that shares the row's pre-edit name via
/// <c>applyForward</c>, including a rename; see <see cref="ChecklistRules.AffectedAllocations"/>.
///
/// The page's stock section — a row is one medication's count across the whole history, not tied
/// to any day — is maintained by <see cref="StocksController"/>, which shares this page's view and
/// its <see cref="MedsPageQueries.MedsPageModelAsync"/> assembly. Both halves meet at
/// <see cref="MedAllocation.MedStockId"/>: an allocation is resolved to a stock row by name when
/// it is written, and every allocation is re-resolved whenever the stocked names change (see
/// <see cref="StocksController.RelinkAllocations"/>). Everything after that point works from the
/// id, which is what lets either side be renamed without disconnecting the doses already logged.
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

        return View(await _db.MedsPageModelAsync(day));
    }

    [HttpPost("/day/{date}/meds")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddAllocation(
        string date,
        string? name,
        string[]? slots,
        string? doseQuantity = null,
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

        var quantityErrors = ChecklistRules.ValidateDoseQuantity(doseQuantity, out var quantity);

        foreach (var error in quantityErrors)
        {
            ModelState.AddModelError(string.Empty, error);
        }

        foreach (var error in ChecklistRules.ValidateRange(rangeFrom, rangeTo))
        {
            ModelState.AddModelError(string.Empty, error);
        }

        if (!ModelState.IsValid)
        {
            return View("Index", await _db.MedsPageModelAsync(
                day, name, chosen, doseQuantity, mealRelation, method, rangeFrom, rangeTo));
        }

        // Non-null: ValidateNewAllocation rejects a name that normalises away.
        var normalizedName = ChecklistRules.NormalizeName(name)!;
        var candidateDays = ChecklistRules.ExpandRange(rangeFrom, rangeTo);
        var existingByDay = await _db.AllocationNamesByDayAsync(rangeFrom, rangeTo);
        var targetDays = ChecklistRules.DaysToAllocate(candidateDays, normalizedName, existingByDay);

        // Resolved once for the whole range: every day gets the same name, so it can only reach
        // the same stock. Null when nothing stocks it, which is not an error — the plan simply
        // draws on no counted supply.
        var stockId = MedStockRules.ResolveStockId(await _db.StockedMedicationsAsync(), normalizedName);

        foreach (var target in targetDays)
        {
            _db.MedAllocations.Add(new MedAllocation
            {
                Day = target,
                Name = normalizedName,
                Slots = chosen,
                DoseQuantity = quantity,
                MedStockId = stockId,
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
        var copied = ChecklistRules.AllocationsToCopy(source, await _db.AllocationNamesAsync(day));
        var stocks = await _db.StockedMedicationsAsync();

        // The plan only, in full: the copies carry the same slots, dose, meal relation and
        // method, and start with nothing ticked however much of the previous day was.
        foreach (var allocation in copied)
        {
            _db.MedAllocations.Add(new MedAllocation
            {
                Day = day,
                Name = allocation.Name,
                Slots = allocation.Slots,
                DoseQuantity = allocation.DoseQuantity,
                // Resolved from the name rather than copied from the source row: the copy is a
                // new plan and links to whatever stocks that medication today, which is right
                // even if the source's own link has since gone stale.
                MedStockId = MedStockRules.ResolveStockId(stocks, allocation.Name),
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

        // The row only. Med entries logged against this medication are the day's record of
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
            DoseQuantity = MedPlanRules.FormatQuantity(allocation.DoseQuantity),
            MealRelation = allocation.MealRelation,
            Method = allocation.Method,
            ApplyForward = false,
            Stocks = await _db.StockRowsAsync()
        });
    }

    [HttpPost("/checklist/{id:int}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(
        int id,
        string? name,
        string[]? slots,
        string? doseQuantity = null,
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
        var stocksForEdit = await _db.StockRowsAsync();

        IActionResult Invalid() => View("Edit", new MedAllocationEditViewModel
        {
            Id = allocation.Id,
            Day = day,
            Name = name,
            Slots = chosen,
            DoseQuantity = doseQuantity ?? string.Empty,
            MealRelation = mealRelation,
            Method = method,
            ApplyForward = applyForward,
            Stocks = stocksForEdit
        });

        // Reuses the add form's name and slot rules. Its duplicate-name check is skipped here
        // (empty list) because "already on this day" means something different for an edit —
        // the rename-collision check below owns that, and excludes the row(s) being edited from
        // themselves so an unchanged name is never flagged.
        foreach (var error in ChecklistRules.ValidateNewAllocation(name, chosen, []))
        {
            ModelState.AddModelError(string.Empty, error);
        }

        var quantityErrors = ChecklistRules.ValidateDoseQuantity(doseQuantity, out var quantity);

        foreach (var error in quantityErrors)
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
            ? await _db.AllocationRefsFromAsync(day)
            : [editedRef];

        var affected = ChecklistRules.AffectedAllocations(editedRef, applyForward, candidates);
        var affectedIds = affected.Select(a => a.Id).ToHashSet();

        var namesByDay = await _db.AllocationRefsByDayAsync(affected.Select(a => a.Day).Distinct().ToList());
        var collisionDays = ChecklistRules.RenameCollisionDays(normalizedName, affectedIds, namesByDay);

        if (collisionDays.Count > 0)
        {
            var labels = collisionDays
                .Select(collision => AppTime.DayLabel(collision, CultureInfo.CurrentUICulture))
                .ToList();
            ModelState.AddModelError(string.Empty,
                $"\"{normalizedName}\" is already used on {ChecklistRules.JoinDayLabels(labels)}.");

            return Invalid();
        }

        var rows = await _db.MedAllocations.Where(a => affectedIds.Contains(a.Id)).ToListAsync();

        // Re-resolved from the name being saved, once for every affected row because they all end
        // up named the same. A rename therefore re-points the plan at whatever stocks the new
        // name, or at nothing — while the doses already ticked keep the id they were stamped
        // with, which is what stops a rename disconnecting them.
        var stockId = MedStockRules.ResolveStockId(await _db.StockedMedicationsAsync(), normalizedName);

        foreach (var row in rows)
        {
            row.Name = normalizedName;
            row.Slots = chosen;
            row.DoseQuantity = quantity;
            row.MedStockId = stockId;
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

    private IActionResult RedirectToDay(DateOnly day) =>
        RedirectToAction(nameof(Index), new { date = AppTime.Key(day) });
}
