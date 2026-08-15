using MedHistory.Models;
using MedHistory.Services;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Data;

/// <summary>
/// Assembles the /day/{date}/meds page's view model. Lives here rather than on either controller
/// because <see cref="MedHistory.Controllers.MedsController"/> (the GET, and a rejected allocation
/// submit) and <see cref="MedHistory.Controllers.StocksController"/> (a rejected stock submit)
/// both render this same page, and a second assembly of it would risk the two drifting apart.
/// </summary>
public static class MedsPageQueries
{
    /// <summary>
    /// What the stock section shows back after one of its own submits was rejected. Bundled
    /// rather than spread over yet more <see cref="MedsPageModelAsync"/> parameters, and absent
    /// entirely — the common case — whenever the page is not answering a bad stock post.
    /// </summary>
    public readonly record struct StockEcho(
        IReadOnlyList<string> Errors,
        string? NewName = null,
        string? NewTotal = null,
        int? RejectedId = null,
        string? RejectedName = null,
        string? RejectedTotal = null);

    public static async Task<MedsViewModel> MedsPageModelAsync(
        this AppDbContext db,
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
        var allocations = await db.MedAllocations
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
            CanCopyPreviousDay = await db.MedAllocations.AnyAsync(a => a.Day == day.AddDays(-1)),
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
            Stocks = await db.StockRowsAsync(),
            StockErrors = stock?.Errors ?? [],
            NewStockName = stock?.NewName,
            NewStockTotal = stock?.NewTotal,
            RejectedStockId = stock?.RejectedId,
            RejectedStockName = stock?.RejectedName,
            RejectedStockTotal = stock?.RejectedTotal
        };
    }
}
