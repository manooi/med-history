using MedHistory.Data;
using MedHistory.Services;
using Microsoft.AspNetCore.Mvc;

namespace MedHistory.Controllers;

/// <summary>
/// Printable date-range summary for a doctor visit: every entry across [From, To], grouped by
/// day, plus the range's per-type totals and how many of its days carry an anxiety vote. Nothing
/// here writes anything — like the type report and search, it only ever reads. Range resolution
/// is <see cref="Services.DoctorReportRules.ResolveRange"/>; the range's data and view-model
/// assembly live in <see cref="DoctorReportQueries.RangeAsync"/>.
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
        var model = await _db.RangeAsync(start, end);
        return View("Index", model);
    }
}
