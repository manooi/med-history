using MedHistory.Services;

namespace MedHistory.Models;

/// <summary>
/// Printable date-range summary for a doctor visit: every entry across the range, grouped by day
/// ascending — chronological reads better on paper than the app's usual newest-first — plus each
/// day's anxiety vote as a text label rather than its emoji, since emoji don't print reliably.
/// Photos are never rendered here, only counted — see
/// <see cref="DoctorReportEntryViewModel.PhotoCount"/>.
/// </summary>
public class DoctorReportViewModel
{
    public required DateOnly From { get; init; }

    public required DateOnly To { get; init; }

    /// <summary>Per-type entry totals for the range — see <see cref="DoctorReportRules.TypeCounts"/>.</summary>
    public required IReadOnlyList<DoctorTypeCount> TypeCounts { get; init; }

    public required int VotedDayCount { get; init; }

    /// <summary>Inclusive day count of the range — the "M" in "N voted / M days".</summary>
    public required int TotalDayCount { get; init; }

    public required IReadOnlyList<DoctorReportDayViewModel> Days { get; init; }
}

/// <summary>One local day's worth of entries, ascending by time within the day, plus that day's
/// anxiety vote if any was cast.</summary>
public class DoctorReportDayViewModel
{
    public required DateOnly Day { get; init; }

    /// <summary>Text label for the day's anxiety vote, e.g. "tense" — null when the day was not voted.</summary>
    public string? AnxietyLabel { get; init; }

    public required IReadOnlyList<DoctorReportEntryViewModel> Entries { get; init; }
}

/// <summary>One entry as the doctor report prints it: no photo thumbnails, just how many the
/// entry carries.</summary>
public class DoctorReportEntryViewModel
{
    public required int Id { get; init; }

    /// <summary>Already converted out of UTC — render as-is.</summary>
    public required DateTimeOffset OccurredAtLocal { get; init; }

    public required string Type { get; init; }

    public string? Detail { get; init; }

    public required int PhotoCount { get; init; }
}
