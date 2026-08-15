using MedHistory.Services;

namespace MedHistory.Models;

/// <summary>
/// The med maintenance page for one day: the plan — what is allocated, when in the day, how
/// much and how it is taken — not the day's progress against it. Progress stays on the day
/// view, derived from the entries the ticks created via <see cref="ChecklistRules.DeriveRows"/>.
///
/// The page also maintains medication stock, which belongs to no day at all: a stock row is one
/// medication's running count over the whole history. It sits here because this is the page the
/// user is on when they think about medication, and it needs no day of its own to do that.
/// </summary>
public class MedsViewModel
{
    public required DateOnly Day { get; init; }

    /// <summary>This day's allocations, in the order they were added.</summary>
    public required IReadOnlyList<MedAllocationRow> Allocations { get; init; }

    /// <summary>True when the previous day has allocations, i.e. copying forward has a source.</summary>
    public required bool CanCopyPreviousDay { get; init; }

    /// <summary>Repopulates the add-medication form when a submit was rejected.</summary>
    public string? NewMedName { get; init; }

    public MedSlots NewMedSlots { get; init; }

    public MealRelation NewMedMealRelation { get; init; }

    public MedMethod NewMedMethod { get; init; }

    /// <summary>Bulk-add range start. Defaults to the page's day — a single-day add.</summary>
    public required DateOnly NewMedFrom { get; init; }

    /// <summary>Bulk-add range end. Defaults to the page's day, i.e. the same single day.</summary>
    public required DateOnly NewMedTo { get; init; }

    /// <summary>
    /// Repopulates the dose field. Held as the raw posted text rather than a decimal so a
    /// rejected submit shows back exactly what was typed, including something unparseable.
    /// </summary>
    public required string NewMedDoseQuantity { get; init; }

    /// <summary>Every tracked stock, with what has been consumed against it, in add order.</summary>
    public required IReadOnlyList<MedStockRow> Stocks { get; init; }

    /// <summary>
    /// Stock validation messages. Deliberately not in ModelState: this page carries two forms,
    /// and a shared ModelState would print a stock complaint above the add-medication form as
    /// well as in the stock section.
    /// </summary>
    public IReadOnlyList<string> StockErrors { get; init; } = [];

    /// <summary>Repopulates the add-stock form when a submit was rejected.</summary>
    public string? NewStockName { get; init; }

    public string? NewStockTotal { get; init; }

    /// <summary>
    /// The stock row whose total was rejected, if any, and what was typed into it — so the
    /// complaint lands next to the input that caused it rather than on a row that saved fine.
    /// </summary>
    public int? RejectedStockId { get; init; }

    public string? RejectedStockTotal { get; init; }

    /// <summary>What one stock row's total input shows: the rejected text, else the stored total.</summary>
    public string StockTotalInput(MedStockRow stock) =>
        RejectedStockId == stock.Id && RejectedStockTotal is not null
            ? RejectedStockTotal
            : MedPlanRules.FormatQuantity(stock.Total);
}

public class MedAllocationRow
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Slot labels in day order — one per expected dose.</summary>
    public required IReadOnlyList<string> SlotLabels { get; init; }

    /// <summary>"×2" when a slot is worth more or less than one unit, empty otherwise.</summary>
    public required string QuantityLabel { get; init; }

    /// <summary>"after meal · eyedrop", or empty when the allocation says nothing beyond its slots.</summary>
    public required string Description { get; init; }
}

/// <summary>
/// The edit form for one allocation: its current plan, and whether saving should also apply the
/// new plan to every future allocation that shares its (pre-edit) name.
/// </summary>
public class MedAllocationEditViewModel
{
    public required int Id { get; init; }

    /// <summary>The row's own day — never changed by an edit, only shown and used to redirect.</summary>
    public required DateOnly Day { get; init; }

    /// <summary>Null only when a rejected submit posted no name at all.</summary>
    public string? Name { get; init; }

    public MedSlots Slots { get; init; }

    /// <summary>Raw posted text, for the same reason <see cref="MedsViewModel.NewMedDoseQuantity"/> is.</summary>
    public required string DoseQuantity { get; init; }

    public MealRelation MealRelation { get; init; }

    public MedMethod Method { get; init; }

    /// <summary>When true, the new plan also replaces every future allocation sharing the row's old name.</summary>
    public bool ApplyForward { get; init; }

    /// <summary>Every tracked stock, for the medication field's datalist suggestions.</summary>
    public required IReadOnlyList<MedStockRow> Stocks { get; init; }
}
