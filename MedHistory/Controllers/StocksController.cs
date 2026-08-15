using MedHistory.Data;
using MedHistory.Models;
using MedHistory.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Controllers;

/// <summary>
/// Maintains medication stock for the /day/{date}/meds page. Stock belongs to no day — a row is
/// one medication's count across the whole history — so these actions carry the page's date only
/// to know where to land afterwards; the day's plan itself lives on <see cref="MedsController"/>.
///
/// Both halves meet at <see cref="MedAllocation.MedStockId"/>: an allocation is resolved to a
/// stock row by name when it is written (<see cref="MedsController"/>), and every allocation is
/// re-resolved here (<see cref="RelinkAllocations"/>) whenever the stocked names change — an add,
/// a rename or a removal. Everything after that point works from the id, which is what lets either
/// side be renamed without disconnecting the doses already logged.
///
/// Renders the same page as <see cref="MedsController.Index"/> when a submit is rejected, via the
/// shared <see cref="MedsPageQueries.MedsPageModelAsync"/> assembly and an explicit view path —
/// the default view lookup would otherwise search Views/Stocks, which has no Index, not Views/Meds.
/// </summary>
public class StocksController : Controller
{
    private readonly AppDbContext _db;
    private readonly ILogger<StocksController> _logger;

    public StocksController(AppDbContext db, ILogger<StocksController> logger)
    {
        _db = db;
        _logger = logger;
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
            return View("~/Views/Meds/Index.cshtml", await _db.MedsPageModelAsync(
                day, stock: new MedsPageQueries.StockEcho(errors, name, total)));
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

            return View("~/Views/Meds/Index.cshtml", await _db.MedsPageModelAsync(day, stock: new MedsPageQueries.StockEcho(
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
            return View("~/Views/Meds/Index.cshtml", await _db.MedsPageModelAsync(day, stock: new MedsPageQueries.StockEcho(
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

            return View("~/Views/Meds/Index.cshtml", await _db.MedsPageModelAsync(day, stock: new MedsPageQueries.StockEcho(
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
        var stocks = await _db.StockedMedicationsAsync();

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

    private IActionResult RedirectToDay(DateOnly day) =>
        RedirectToAction(nameof(MedsController.Index), "Meds", new { date = AppTime.Key(day) });
}
