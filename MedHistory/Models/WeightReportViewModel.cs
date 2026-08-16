using MedHistory.Services;

namespace MedHistory.Models;

/// <summary>
/// The month-calendar weight report. The month itself is held as the rule type rather than
/// copied into a view-specific one — see <see cref="AnxietyReportViewModel"/>, which does the
/// same for the anxiety report.
/// </summary>
public class WeightReportViewModel
{
    public required MeasurementMonth Month { get; init; }

    /// <summary>Marked on the grid, so the reader can place themselves in the month at a glance.</summary>
    public required DateOnly Today { get; init; }

    public bool IsCurrentMonth => ReportRules.FirstOfMonth(Today) == Month.FirstDay;
}
