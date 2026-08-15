using MedHistory.Models;
using MedHistory.Services;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Data;

/// <summary>
/// The database side of the anxiety report's month calendar: one query for the month's votes,
/// handed to <see cref="AnxietyRules"/> to build the grid. Lives here so
/// <see cref="MedHistory.Controllers.AnxietyReportController"/> stays route parsing and view
/// selection only — the same split <see cref="ReportQueries"/> makes for the med report.
/// </summary>
public static class AnxietyQueries
{
    public static async Task<AnxietyReportViewModel> MonthAsync(this AppDbContext db, DateOnly anyDayOfMonth)
    {
        // Normalised here so the query range is the calendar month whatever the caller held.
        var month = ReportRules.FirstOfMonth(anyDayOfMonth);
        var next = month.AddMonths(1);

        var votes = await db.AnxietyVotes
            .AsNoTracking()
            .Where(v => v.Day >= month && v.Day < next)
            .ToListAsync();

        return new AnxietyReportViewModel
        {
            Month = AnxietyRules.BuildMonth(month, votes),
            Today = AppTime.Today()
        };
    }
}
