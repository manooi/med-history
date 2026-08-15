using MedHistory.Models;
using MedHistory.Services;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Data;

/// <summary>
/// The database side of the doctor report: every entry in a resolved date range, grouped by day,
/// plus the range's per-type totals and voted-anxiety-day count. Lives here so
/// <see cref="MedHistory.Controllers.DoctorReportController"/> stays route parsing and view
/// selection only — see <see cref="DoctorReportRules"/>, which owns how the range and the summary
/// counts are decided.
/// </summary>
public static class DoctorReportQueries
{
    public static async Task<DoctorReportViewModel> RangeAsync(this AppDbContext db, DateOnly start, DateOnly end)
    {
        var (rangeStart, _) = AppTime.DayRange(start);
        var (_, rangeEnd) = AppTime.DayRange(end);

        // Projected rather than Include'd: photo bytes are never needed here, and the printed
        // page only ever shows how many photos an entry carries — never the photos themselves —
        // so a count is all the query asks for, not the ids a thumbnail grid would need.
        var rows = await db.Entries
            .AsNoTracking()
            .Where(e => e.OccurredAt >= rangeStart && e.OccurredAt < rangeEnd)
            .Select(e => new
            {
                e.Id,
                e.OccurredAt,
                e.Type,
                e.Severity,
                e.PillName,
                e.Note,
                PhotoCount = e.Photos.Count
            })
            .ToListAsync();

        var votes = await db.AnxietyVotes
            .AsNoTracking()
            .Where(v => v.Day >= start && v.Day <= end)
            .ToListAsync();

        var voteByDay = votes.ToDictionary(v => v.Day, v => v.Level);

        // Ascending throughout — day order and, within a day, time order — unlike every other
        // report in the app, which reads newest first. A page meant to be read start-to-finish
        // on paper belongs in the order the visit actually happened.
        var days = rows
            .OrderBy(r => r.OccurredAt)
            .GroupBy(r => AppTime.DayOf(r.OccurredAt))
            .OrderBy(group => group.Key)
            .Select(group => new DoctorReportDayViewModel
            {
                Day = group.Key,
                AnxietyLabel = voteByDay.TryGetValue(group.Key, out var level) ? AnxietyRules.Label(level) : null,
                Entries = group.Select(r => new DoctorReportEntryViewModel
                {
                    Id = r.Id,
                    OccurredAtLocal = AppTime.ToLocal(r.OccurredAt),
                    Type = r.Type,
                    Detail = EntryRules.DetailLine(r.Type, r.Severity, r.PillName, r.Note),
                    PhotoCount = r.PhotoCount
                }).ToList()
            })
            .ToList();

        return new DoctorReportViewModel
        {
            From = start,
            To = end,
            TypeCounts = DoctorReportRules.TypeCounts(rows.Select(r => r.Type)),
            VotedDayCount = DoctorReportRules.VotedDayCount(votes.Select(v => v.Day), start, end),
            TotalDayCount = DoctorReportRules.TotalDays(start, end),
            Days = days
        };
    }
}
