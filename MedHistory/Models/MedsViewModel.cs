using MedHistory.Services;

namespace MedHistory.Models;

/// <summary>
/// The med maintenance page for one day: the plan — what is allocated, when in the day, and
/// how it is taken — not the day's progress against it. Progress stays on the day view,
/// derived from the entries the ticks created via <see cref="ChecklistRules.DeriveRows"/>.
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
}

public class MedAllocationRow
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Slot labels in day order — one per expected dose.</summary>
    public required IReadOnlyList<string> SlotLabels { get; init; }

    /// <summary>"after meal · eyedrop", or empty when the allocation says nothing beyond its slots.</summary>
    public required string Description { get; init; }
}
