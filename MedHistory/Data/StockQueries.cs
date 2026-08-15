using MedHistory.Models;
using MedHistory.Services;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Data;

/// <summary>
/// The database side of medication stock: one read of the stock rows and one grouped read of
/// every dose logged against them. It lives here rather than in a controller because the meds
/// page and the day view both need exactly this, and a stock count that differed between the
/// two pages would be worse than no count at all.
/// </summary>
public static class StockQueries
{
    /// <summary>
    /// Every stock row with what has been consumed against it, in the order the rows were
    /// added. Two queries at most, whatever the number of stock rows or entries — and only one
    /// when nothing is stocked, which is the state the app starts in and stays in until the
    /// user tracks something.
    /// </summary>
    public static async Task<IReadOnlyList<MedStockRow>> StockRowsAsync(this AppDbContext db)
    {
        var stocks = await db.MedStocks.AsNoTracking().OrderBy(s => s.Id).ToListAsync();

        return stocks.Count == 0 ? [] : MedStockRules.DeriveRows(stocks, await db.PillUsageAsync());
    }

    /// <summary>
    /// What every dose has been logged against, summed in the database: one row per distinct
    /// (stock link, medication name) pair, not one query per stock row.
    ///
    /// Both halves are the grouping key because both are how a dose finds its stock — the id a
    /// tick stamped on it, or, failing that, the name it was written with. Grouping on the pair
    /// rather than reading each route separately keeps this to one query and leaves
    /// <see cref="MedStockRules.DeriveRows"/> to decide which half of each group applies.
    ///
    /// Names are grouped exactly as stored, so casing and stray spaces still produce separate rows
    /// here; folding those together is <see cref="MedStockRules.DeriveRows"/>'s job too, which
    /// keeps name matching a single rule in the app rather than one that half depends on the
    /// database's idea of lower-casing.
    /// </summary>
    private static async Task<IReadOnlyList<MedUsage>> PillUsageAsync(this AppDbContext db)
    {
        var rows = await db.Entries
            .AsNoTracking()
            // A dose with neither a link nor a name can draw down nothing, so it is not read.
            .Where(e => e.Type == BuiltInEntryTypes.Med && (e.MedStockId != null || e.PillName != null))
            .GroupBy(e => new { e.MedStockId, e.PillName })
            .Select(g => new
            {
                g.Key.MedStockId,
                g.Key.PillName,
                Quantity = g.Sum(e => e.DoseQuantity ?? MedPlanRules.DefaultDoseQuantity)
            })
            .ToListAsync();

        return rows.Select(r => new MedUsage(r.MedStockId, r.PillName, r.Quantity)).ToList();
    }
}
