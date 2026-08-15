using MedHistory.Data;
using MedHistory.Services;
using Microsoft.AspNetCore.Mvc;

namespace MedHistory.Controllers;

/// <summary>
/// The month calendar of medication adherence — every medication at once, one cell per day.
/// Read-only: nothing here changes a plan or a dose, and every cell links back to the day page
/// where both are edited. The month's data and view-model assembly live in
/// <see cref="ReportQueries.MedMonthAsync"/> — this controller is route parsing and view
/// selection only.
/// </summary>
public class ReportController : Controller
{
    private readonly AppDbContext _db;

    public ReportController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("/med-report")]
    public Task<IActionResult> Index() => ShowMonth(ReportRules.FirstOfMonth(AppTime.Today()));

    [HttpGet("/med-report/{ym}")]
    public Task<IActionResult> ByMonth(string ym)
    {
        if (!ReportRules.TryParseMonth(ym, out var month))
        {
            // A hand-typed month is the only way to get here with garbage, and the current month
            // is a better answer than an error page — same as the day page's bad date.
            return Task.FromResult<IActionResult>(RedirectToAction(nameof(Index)));
        }

        return ShowMonth(month);
    }

    private async Task<IActionResult> ShowMonth(DateOnly anyDayOfMonth)
    {
        var model = await _db.MedMonthAsync(anyDayOfMonth);
        return View("Index", model);
    }
}
