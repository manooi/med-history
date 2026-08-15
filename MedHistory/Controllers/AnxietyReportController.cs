using MedHistory.Data;
using MedHistory.Models;
using MedHistory.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Controllers;

/// <summary>
/// The month calendar of anxiety votes, one cell per day. Read-only: nothing here changes a
/// vote, and every voted cell links back to the day page where the vote itself is set — see
/// <see cref="DayController.Vote"/>. Shaped the same way <see cref="ReportController"/> shapes
/// the med report: one query for the month, handed to a pure rules method that owns the grid.
/// </summary>
public class AnxietyReportController : Controller
{
    private readonly AppDbContext _db;

    public AnxietyReportController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("/anxiety-report")]
    public Task<IActionResult> Index() => ShowMonth(ReportRules.FirstOfMonth(AppTime.Today()));

    [HttpGet("/anxiety-report/{ym}")]
    public Task<IActionResult> ByMonth(string ym)
    {
        if (!ReportRules.TryParseMonth(ym, out var month))
        {
            // A hand-typed month is the only way to get here with garbage, and the current
            // month is a better answer than an error page — same as the med report's bad month.
            return Task.FromResult<IActionResult>(RedirectToAction(nameof(Index)));
        }

        return ShowMonth(month);
    }

    private async Task<IActionResult> ShowMonth(DateOnly anyDayOfMonth)
    {
        // Normalised here so the query range is the calendar month whatever the caller held.
        var month = ReportRules.FirstOfMonth(anyDayOfMonth);
        var next = month.AddMonths(1);

        var votes = await _db.AnxietyVotes
            .AsNoTracking()
            .Where(v => v.Day >= month && v.Day < next)
            .ToListAsync();

        var model = new AnxietyReportViewModel
        {
            Month = AnxietyRules.BuildMonth(month, votes),
            Today = AppTime.Today()
        };

        return View("Index", model);
    }
}
