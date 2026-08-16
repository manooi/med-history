using MedHistory.Data;
using MedHistory.Services;
using Microsoft.AspNetCore.Mvc;

namespace MedHistory.Controllers;

/// <summary>
/// The month calendar of weight readings, one cell per day. Read-only: nothing here changes a
/// reading, and every cell links back to the day page where readings are logged — see
/// <see cref="DayController.AddWeight"/>. The month's data and view-model assembly live in
/// <see cref="WeightReportQueries.WeightMonthAsync"/> — this controller is route parsing and view
/// selection only, the same split <see cref="AnxietyReportController"/> makes.
/// </summary>
public class WeightReportController : Controller
{
    private readonly AppDbContext _db;

    public WeightReportController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("/weight-report")]
    public Task<IActionResult> Index() => ShowMonth(ReportRules.FirstOfMonth(AppTime.Today()));

    [HttpGet("/weight-report/{ym}")]
    public Task<IActionResult> ByMonth(string ym)
    {
        if (!ReportRules.TryParseMonth(ym, out var month))
        {
            // A hand-typed month is the only way to get here with garbage, and the current
            // month is a better answer than an error page — same as the other reports' bad month.
            return Task.FromResult<IActionResult>(RedirectToAction(nameof(Index)));
        }

        return ShowMonth(month);
    }

    private async Task<IActionResult> ShowMonth(DateOnly anyDayOfMonth)
    {
        var model = await _db.WeightMonthAsync(anyDayOfMonth);
        return View("Index", model);
    }
}
