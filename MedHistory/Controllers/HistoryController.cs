using MedHistory.Data;
using MedHistory.Models;
using MedHistory.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Controllers;

public class HistoryController : Controller
{
    private readonly AppDbContext _db;

    public HistoryController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("/history")]
    public async Task<IActionResult> Index()
    {
        // Projected rather than Include'd: Note/Photos are never needed for the
        // day-by-day summary, only the timestamp and type.
        var rows = await _db.Entries
            .Select(e => new { e.OccurredAt, e.Type })
            .ToListAsync();

        var offset = TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.UtcNow);
        var grouped = HistoryRules.GroupByDay(
            rows.Select(r => (r.OccurredAt, r.Type)),
            offset);

        var model = new HistoryViewModel
        {
            Days = grouped.Select(d => new HistoryDayViewModel
            {
                Day = d.Day,
                Counts = d.Counts
                    .Select(c => new HistoryTypeCount { Type = c.Key, Count = c.Value })
                    .OrderBy(c => c.Type)
                    .ToList()
            }).ToList()
        };

        return View(model);
    }
}
