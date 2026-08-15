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
/// The page's stock section is maintained here too. Stock belongs to no day — a row is one
/// medication's count across the whole history — so its actions carry the page's date only to
/// know where to land afterwards.
///
/// Both halves meet at <see cref="MedAllocation.MedStockId"/>: an allocation is resolved to a
/// stock row by name when it is written, and every allocation is re-resolved whenever the stocked
/// names change. Everything after that point works from the id, which is what lets either side be
/// renamed without disconnecting the doses already logged.
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
            return View("Index", await BuildModel(
                day, name, chosen, doseQuantity, mealRelation, method, rangeFrom, rangeTo));
        }

        // Non-null: ValidateNewAllocation rejects a name that normalises away.
        var normalizedName = ChecklistRules.NormalizeName(name)!;
        var candidateDays = ChecklistRules.ExpandRange(rangeFrom, rangeTo);
        var existingByDay = await AllocationNamesByDay(rangeFrom, rangeTo);
        var targetDays = ChecklistRules.DaysToAllocate(candidateDays, normalizedName, existingByDay);

        // Resolved once for the whole range: every day gets the same name, so it can only reach
        // the same stock. Null when nothing stocks it, which is not an error — the plan simply
        // draws on no counted supply.
        var stockId = MedStockRules.ResolveStockId(await StockedMedications(), normalizedName);

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
        var copied = ChecklistRules.AllocationsToCopy(source, await AllocationNames(day));
        var stocks = await StockedMedications();

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

        // Re-resolved from the name being saved, once for every affected row because they all end
        // up named the same. A rename therefore re-points the plan at whatever stocks the new
        // name, or at nothing — while the doses already ticked keep the id they were stamped
        // with, which is what stops a rename disconnecting them.
        var stockId = MedStockRules.ResolveStockId(await StockedMedications(), normalizedName);

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

    [HttpPost("/day/{date}/meds/stock")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddStock(string date, string? name, string? total)
    {
        if (!AppTime.TryParseDay(date, out var day))
        {
            return RedirectToAction(nameof(DayController.Index), "Day");
        }

        var existingNames = await _db.MedStocks.AsNoTracking().Select(s => s.Name).ToListAsync();
        var errors = MedStockRules.ValidateStock(name, total, existingNames, out var parsedTotal);

        if (errors.Count > 0)
        {
            return View("Index", await BuildModel(day, stock: new StockEcho(errors, name, total)));
        }

        // Non-null: ValidateStock rejects a name that normalises away.
        var normalizedName = MedStockRules.NormalizeName(name)!;

        _db.MedStocks.Add(new MedStock { Name = normalizedName, TotalCount = parsedTotal });

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // The unique index on lower(Name) is the real guard; the check above only beats it
            // if two adds race, which is worth a readable message rather than a 500.
            _db.ChangeTracker.Clear();

            return View("Index", await BuildModel(day, stock: new StockEcho(
                [$"\"{normalizedName}\" is already stocked."], name, total)));
        }

        // A new row may claim a name the plan was already using unlinked, so the links are
        // re-resolved before anything reads them.
        await RelinkAllocations();

        // Ids and counts only — a medication name is health data and stays out of the log.
        _logger.LogInformation("Stock row added");

        return RedirectToDay(day);
    }

    [HttpPost("/day/{date}/meds/stock/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStock(string date, int id, string? name, string? total)
    {
        if (!AppTime.TryParseDay(date, out var day))
        {
            return RedirectToAction(nameof(DayController.Index), "Day");
        }

        var stock = await _db.MedStocks.FindAsync(id);

        if (stock is null)
        {
            return NotFound();
        }

        // Both halves are editable: a refill is the total going up, and a rename is now safe
        // because the doses ticked against this row carry its id, not its name — they stay
        // counted here whatever it is called. Hand-typed doses follow the name instead and move
        // with it, which is the only thing a rename can change.
        //
        // The duplicate check excludes this row so leaving the name alone is not read as a
        // collision with itself.
        var otherNames = await _db.MedStocks
            .AsNoTracking()
            .Where(s => s.Id != id)
            .Select(s => s.Name)
            .ToListAsync();

        var errors = MedStockRules.ValidateStock(name, total, otherNames, out var parsedTotal);

        if (errors.Count > 0)
        {
            return View("Index", await BuildModel(day, stock: new StockEcho(
                errors, RejectedId: id, RejectedName: name, RejectedTotal: total)));
        }

        // Non-null: ValidateStock rejects a name that normalises away.
        var normalizedName = MedStockRules.NormalizeName(name)!;

        stock.Name = normalizedName;
        stock.TotalCount = parsedTotal;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Same guard AddStock has, for the same reason: the unique index on lower(Name) is
            // the real one, and losing a race to it deserves a readable message, not a 500.
            _db.ChangeTracker.Clear();

            return View("Index", await BuildModel(day, stock: new StockEcho(
                [$"\"{normalizedName}\" is already stocked."],
                RejectedId: id, RejectedName: name, RejectedTotal: total)));
        }

        // A rename moves a name off this row and possibly onto it from elsewhere, so the plan's
        // links are re-resolved before anything reads them. Entries are deliberately untouched —
        // their stamped ids are what keeps the history attached across the rename.
        await RelinkAllocations();

        _logger.LogInformation("Stock {StockId} updated", id);

        return RedirectToDay(day);
    }

    [HttpPost("/day/{date}/meds/stock/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveStock(string date, int id)
    {
        if (!AppTime.TryParseDay(date, out var day))
        {
            return RedirectToAction(nameof(DayController.Index), "Day");
        }

        var stock = await _db.MedStocks.FindAsync(id);

        if (stock is null)
        {
            return NotFound();
        }

        // The row only. The doses counted against it are entries in their own right and stay
        // exactly where they are; removing the row just stops the app counting them. Their
        // stamped stock ids are left dangling on purpose — pointing at nothing is what "no
        // longer tracked" means, and re-adding the row under the same name is not meant to
        // silently reclaim a history the user stopped counting.
        _db.MedStocks.Remove(stock);
        await _db.SaveChangesAsync();

        // The plan is a different matter: an allocation pointing at a row that is gone would
        // show no count while still naming the medication, so its link is dropped to null here.
        await RelinkAllocations();

        _logger.LogInformation("Stock {StockId} removed", id);

        return RedirectToDay(day);
    }

    /// <summary>
    /// What the stock section shows back after one of its own submits was rejected. Bundled
    /// rather than spread over yet more <see cref="BuildModel"/> parameters, and absent
    /// entirely — the common case — whenever the page is not answering a bad stock post.
    /// </summary>
    private readonly record struct StockEcho(
        IReadOnlyList<string> Errors,
        string? NewName = null,
        string? NewTotal = null,
        int? RejectedId = null,
        string? RejectedName = null,
        string? RejectedTotal = null);

    /// <summary>
    /// Every stocked medication, for turning a name into a stock link. Read whole because that is
    /// what <see cref="MedStockRules.ResolveStockId"/> matches against and the table holds one
    /// person's medications — a query per name would be the expensive way round.
    /// </summary>
    private async Task<List<MedStock>> StockedMedications() =>
        await _db.MedStocks.AsNoTracking().ToListAsync();

    /// <summary>
    /// Re-points every allocation at the stock its name now names, and saves. Run after any change
    /// to the stocked names — an add, a rename or a removal — because all three can move a name
    /// onto a row or off it, and an allocation left pointing at the wrong one would stamp that
    /// wrong id onto the next dose ticked.
    ///
    /// Sweeping every allocation rather than the ones naming the changed row is deliberate: it is
    /// one pass over one person's plan and it cannot miss a case. Only the rows whose link
    /// actually changes are written.
    /// </summary>
    private async Task RelinkAllocations()
    {
        var stocks = await StockedMedications();

        var links = await _db.MedAllocations
            .AsNoTracking()
            .Select(a => new StockLink(a.Id, a.Name, a.MedStockId))
            .ToListAsync();

        var changed = MedStockRules.Relink(links, stocks);

        if (changed.Count == 0)
        {
            return;
        }

        var resolved = changed.ToDictionary(c => c.AllocationId, c => c.StockId);
        var ids = resolved.Keys.ToList();
        var rows = await _db.MedAllocations.Where(a => ids.Contains(a.Id)).ToListAsync();

        foreach (var row in rows)
        {
            row.MedStockId = resolved[row.Id];
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation("{Count} allocation(s) re-linked to stock", rows.Count);
    }

    private async Task<MedsViewModel> BuildModel(
        DateOnly day,
        string? newMedName = null,
        MedSlots newMedSlots = MedSlots.None,
        string? newMedDoseQuantity = null,
        MealRelation newMedMealRelation = MealRelation.None,
        MedMethod newMedMethod = MedMethod.Eat,
        DateOnly? newMedFrom = null,
        DateOnly? newMedTo = null,
        StockEcho? stock = null)
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
                QuantityLabel = MedPlanRules.QuantityLabel(a.DoseQuantity),
                Description = MedPlanRules.DescribeAllocation(a.MealRelation, a.Method)
            }).ToList(),
            CanCopyPreviousDay = await _db.MedAllocations.AnyAsync(a => a.Day == day.AddDays(-1)),
            NewMedName = newMedName,
            NewMedSlots = newMedSlots,
            // Un-set only when the GET view builds the form fresh, which is the one time the
            // page chooses the value; a rejected submit echoes back exactly what was typed.
            NewMedDoseQuantity = newMedDoseQuantity
                ?? MedPlanRules.FormatQuantity(MedPlanRules.DefaultDoseQuantity),
            NewMedMealRelation = newMedMealRelation,
            NewMedMethod = newMedMethod,
            NewMedFrom = newMedFrom ?? day,
            NewMedTo = newMedTo ?? day,
            Stocks = await _db.StockRowsAsync(),
            StockErrors = stock?.Errors ?? [],
            NewStockName = stock?.NewName,
            NewStockTotal = stock?.NewTotal,
            RejectedStockId = stock?.RejectedId,
            RejectedStockName = stock?.RejectedName,
            RejectedStockTotal = stock?.RejectedTotal
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
