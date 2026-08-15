using MedHistory.Services;

namespace MedHistory.Models;

public class DayViewModel
{
    public required DateOnly Day { get; init; }

    public required bool IsToday { get; init; }

    public required IReadOnlyList<DayEntryViewModel> Entries { get; init; }

    /// <summary>Active type names, already in the order the "+" buttons should appear.</summary>
    public required IReadOnlyList<string> NewEntryTypes { get; init; }

    /// <summary>
    /// The day's medication checklist, in the order the medications were added. Held as the
    /// rule type rather than copied into a view-specific one: every field the row renders is
    /// already derived state straight out of <see cref="ChecklistRules.DeriveProgress"/>.
    /// </summary>
    public required IReadOnlyList<ChecklistProgress> Checklist { get; init; }

    /// <summary>True when the previous day has allocations, i.e. copying forward has a source.</summary>
    public required bool CanCopyPreviousDay { get; init; }

    /// <summary>Repopulates the add-medication form when a submit was rejected.</summary>
    public string? NewMedName { get; init; }

    public int NewMedRequiredCount { get; init; } = ChecklistRules.MinRequiredCount;

    public DateOnly PreviousDay => Day.AddDays(-1);

    public DateOnly NextDay => Day.AddDays(1);
}

public class DayEntryViewModel
{
    public required int Id { get; init; }

    /// <summary>Already converted out of UTC — render as-is.</summary>
    public required DateTimeOffset OccurredAtLocal { get; init; }

    public required string Type { get; init; }

    public string? Detail { get; init; }

    /// <summary>Ids only — thumbnails are fetched by the browser via GET /photos/{id}.</summary>
    public required IReadOnlyList<int> PhotoIds { get; init; }
}
