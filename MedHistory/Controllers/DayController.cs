using MedHistory.Data;
using MedHistory.Models;
using MedHistory.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Controllers;

public class DayController : Controller
{
    private readonly AppDbContext _db;

    public DayController(AppDbContext db)
    {
        _db = db;
    }

    // GET / — today, via the default conventional route.
    public Task<IActionResult> Index() => ShowDay(AppTime.Today());

    [HttpGet("/day/{date}")]
    public Task<IActionResult> ByDate(string date)
    {
        if (!AppTime.TryParseDay(date, out var day))
        {
            return Task.FromResult<IActionResult>(RedirectToAction(nameof(Index)));
        }

        return ShowDay(day);
    }

    private async Task<IActionResult> ShowDay(DateOnly day)
    {
        var (start, end) = AppTime.DayRange(day);

        // Projected rather than Include'd: the photo bytes are never needed here,
        // only the ids so the view can request thumbnails via GET /photos/{id}.
        var rows = await _db.Entries
            .Where(e => e.OccurredAt >= start && e.OccurredAt < end)
            .OrderBy(e => e.OccurredAt)
            .Select(e => new
            {
                e.Id,
                e.OccurredAt,
                e.Type,
                e.Note,
                e.Severity,
                e.PillName,
                PhotoIds = e.Photos.Select(p => p.Id).ToList()
            })
            .ToListAsync();

        var model = new DayViewModel
        {
            Day = day,
            IsToday = day == AppTime.Today(),
            Entries = rows.Select(r => new DayEntryViewModel
            {
                Id = r.Id,
                OccurredAtLocal = AppTime.ToLocal(r.OccurredAt),
                Type = r.Type,
                Detail = EntryRules.DetailLine(r.Type, r.Severity, r.PillName, r.Note),
                PhotoIds = r.PhotoIds
            }).ToList()
        };

        return View("Index", model);
    }
}
