using MedHistory.Data;
using MedHistory.Services;
using Microsoft.AspNetCore.Mvc;

namespace MedHistory.Controllers;

/// <summary>
/// The month calendar of anxiety votes, one cell per day. Read-only: nothing here changes a
/// vote, and every voted cell links back to the day page where the vote itself is set — see
/// <see cref="DayController.Vote"/>. The month's data and view-model assembly live in
/// <see cref="AnxietyQueries.MonthAsync"/> — this controller is route parsing and view selection
/// only, the same split <see cref="ReportController"/> makes for the med report.
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
        var model = await _db.MonthAsync(anyDayOfMonth);
        return View("Index", model);
    }
}
