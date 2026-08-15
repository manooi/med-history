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
    /// already derived state straight out of <see cref="ChecklistRules.DeriveRows"/>.
    /// Adding, removing and copying forward allocations lives on <see cref="MedsViewModel"/>.
    /// </summary>
    public required IReadOnlyList<ChecklistRow> Checklist { get; init; }

    /// <summary>The day's anxiety vote, or null when nothing has been voted yet.</summary>
    public required AnxietyLevel? AnxietyLevel { get; init; }

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
