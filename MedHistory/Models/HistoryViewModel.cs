namespace MedHistory.Models;

public class HistoryViewModel
{
    public required IReadOnlyList<HistoryDayViewModel> Days { get; init; }
}

public class HistoryDayViewModel
{
    public required DateOnly Day { get; init; }

    /// <summary>Only types with a count greater than zero.</summary>
    public required IReadOnlyList<HistoryTypeCount> Counts { get; init; }
}

public class HistoryTypeCount
{
    public required string Type { get; init; }

    public required int Count { get; init; }
}
