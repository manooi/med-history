namespace MedHistory.Models;

public class DayViewModel
{
    public required DateOnly Day { get; init; }

    public required bool IsToday { get; init; }

    public required IReadOnlyList<DayEntryViewModel> Entries { get; init; }

    public DateOnly PreviousDay => Day.AddDays(-1);

    public DateOnly NextDay => Day.AddDays(1);
}

public class DayEntryViewModel
{
    public required int Id { get; init; }

    /// <summary>Already converted out of UTC — render as-is.</summary>
    public required DateTimeOffset OccurredAtLocal { get; init; }

    public required EntryType Type { get; init; }

    public string? Detail { get; init; }

    /// <summary>Ids only — thumbnails are fetched by the browser via GET /photos/{id}.</summary>
    public required IReadOnlyList<int> PhotoIds { get; init; }
}
