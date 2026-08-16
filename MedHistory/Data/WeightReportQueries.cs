using MedHistory.Models;
using MedHistory.Services;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Data;

/// <summary>
/// The database side of the weight report's month calendar: one query for the month's weight
/// readings, handed to <see cref="MeasurementRules"/> to build the grid and stats. Lives here so
/// <see cref="MedHistory.Controllers.WeightReportController"/> stays route parsing and view
/// selection only — the same split <see cref="AnxietyQueries"/> makes for the anxiety report.
/// </summary>
public static class WeightReportQueries
{
    public static async Task<WeightReportViewModel> WeightMonthAsync(this AppDbContext db, DateOnly anyDayOfMonth)
    {
        // Normalised here so the query range is the calendar month whatever the caller held.
        var month = ReportRules.FirstOfMonth(anyDayOfMonth);
        var next = month.AddMonths(1);

        // Instants, not calendar days: OccurredAt is a timestamptz, unlike AnxietyVote.Day, so
        // the range has to go through the same local-day conversion the day page uses.
        var (start, _) = AppTime.DayRange(month);
        var (end, _) = AppTime.DayRange(next);

        var rows = await db.Measurements
            .AsNoTracking()
            .Where(m => m.Kind == MeasurementKinds.Weight && m.OccurredAt >= start && m.OccurredAt < end)
            .Select(m => new { m.Value, m.OccurredAt })
            .ToListAsync();

        var readings = rows.Select(r => new MeasurementReading(AppTime.DayOf(r.OccurredAt), r.Value, r.OccurredAt));

        return new WeightReportViewModel
        {
            Month = MeasurementRules.BuildMonth(month, readings),
            Today = AppTime.Today()
        };
    }
}
