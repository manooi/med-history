using MedHistory.Services;

namespace MedHistory.Models;

/// <summary>
/// The med maintenance page for one day: the plan (what's allocated and how many times),
/// not the day's progress against it — that stays on the day view, derived the same way
/// as ever via <see cref="ChecklistRules.DeriveProgress"/>.
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

    public int NewMedRequiredCount { get; init; } = ChecklistRules.MinRequiredCount;
}

public class MedAllocationRow
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public required int RequiredCount { get; init; }
}
