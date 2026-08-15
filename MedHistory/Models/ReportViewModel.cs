using MedHistory.Services;

namespace MedHistory.Models;

/// <summary>
/// The month-calendar adherence report. The month itself is held as the rule type rather than
/// copied into a view-specific one: every cell, total and month key the page renders is already
/// derived state straight out of <see cref="ReportRules.BuildMonth"/>.
/// </summary>
public class ReportViewModel
{
    public required ReportMonth Month { get; init; }

    /// <summary>Marked on the grid, so the reader can place themselves in the month at a glance.</summary>
    public required DateOnly Today { get; init; }

    public bool IsCurrentMonth => ReportRules.FirstOfMonth(Today) == Month.FirstDay;
}
