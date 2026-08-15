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

    public MealRelation MealRelation { get; init; }

    public MedMethod Method { get; init; }

    /// <summary>When true, the new plan also replaces every future allocation sharing the row's old name.</summary>
    public bool ApplyForward { get; init; }
}
