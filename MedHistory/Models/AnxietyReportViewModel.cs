using MedHistory.Services;

namespace MedHistory.Models;

/// <summary>
/// The month-calendar anxiety report. The month itself is held as the rule type rather than
/// copied into a view-specific one: every cell, count and month key the page renders is already
/// derived state straight out of <see cref="AnxietyRules.BuildMonth"/> — see
/// <see cref="ReportViewModel"/>, which does the same for the med report.
/// </summary>
public class AnxietyReportViewModel
{
    public required AnxietyMonth Month { get; init; }

    /// <summary>Marked on the grid, so the reader can place themselves in the month at a glance.</summary>
    public required DateOnly Today { get; init; }

    public bool IsCurrentMonth => ReportRules.FirstOfMonth(Today) == Month.FirstDay;
}
