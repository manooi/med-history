using MedHistory.Data;
using MedHistory.Models;
using MedHistory.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Controllers;

/// <summary>
/// Printable date-range summary for a doctor visit: every entry across [From, To], grouped by
/// day, plus the range's per-type totals and how many of its days carry an anxiety vote. Nothing
/// here writes anything — like the type report and search, it only ever reads. See
/// <see cref="Services.DoctorReportRules"/> for how the range and the summary counts are decided.
/// </summary>
public class DoctorReportController : Controller
{
    private readonly AppDbContext _db;

    public DoctorReportController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("/doctor-report")]
    public async Task<IActionResult> Index([FromQuery] string? from, [FromQuery] string? to)
    {
        var (start, end) = DoctorReportRules.ResolveRange(from, to, AppTime.Today());

        var (rangeStart, _) = AppTime.DayRange(start);
        var (_, rangeEnd) = AppTime.DayRange(end);

        // Projected rather than Include'd: photo bytes are never needed here, and the printed
        // page only ever shows how many photos an entry carries — never the photos themselves —
        // so a count is all the query asks for, not the ids a thumbnail grid would need.
        var rows = await _db.Entries
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

        var votes = await _db.AnxietyVotes
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

        var model = new DoctorReportViewModel
        {
            From = start,
            To = end,
            TypeCounts = DoctorReportRules.TypeCounts(rows.Select(r => r.Type)),
            VotedDayCount = DoctorReportRules.VotedDayCount(votes.Select(v => v.Day), start, end),
            TotalDayCount = DoctorReportRules.TotalDays(start, end),
            Days = days
        };

        return View("Index", model);
    }
}
