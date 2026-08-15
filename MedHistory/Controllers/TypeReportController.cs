using MedHistory.Data;
using MedHistory.Models;
using MedHistory.Services;
using Microsoft.AspNetCore.Mvc;

namespace MedHistory.Controllers;

/// <summary>
/// Read-only history of one entry type across every day it was ever logged, newest day first.
/// A mirror image of the day page: that page is every type on one day, this page is one type
/// across every day. Nothing here writes anything — every row still links back to
/// <c>/entries/{id}/edit</c>, which is where an entry is actually changed.
///
/// The queries and view-model assembly live in <see cref="TypeReportQueries"/>; this controller
/// keeps the two decisions that depend on what a query finds — an unrecognised type name (matched
/// here, since <see cref="TypeReportQueries.AllTypeNamesAsync"/> is cheap and the redirect needs
/// it before anything else runs) redirects to the selector, and an out-of-range page (learned
/// from <see cref="TypeReportQueries.PageAsync"/>'s already-clamped <c>Page</c>) redirects to the
/// clamped one.
/// </summary>
public class TypeReportController : Controller
{
    private readonly AppDbContext _db;

    public TypeReportController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("/type-report")]
    public async Task<IActionResult> Index()
    {
        var model = new TypeReportViewModel
        {
            AllTypeNames = await _db.AllTypeNamesAsync(),
            CurrentType = null,
            Days = [],
            Page = 1,
            PageCount = 0
        };

        return View("Index", model);
    }

    [HttpGet("/type-report/{type}")]
    public async Task<IActionResult> ByType(string type, [FromQuery] int page = 1)
    {
        var typeNames = await _db.AllTypeNamesAsync();

        // Matched case-insensitively, like every other type lookup in the app, but the entries
        // themselves are queried by the stored row's own casing — Entry.Type is compared
        // ordinal, and a hand-typed URL must not be able to miss a type by casing alone.
        var canonical = typeNames.FirstOrDefault(name => EntryTypeRules.NamesMatch(name, type));

        if (canonical is null)
        {
            return RedirectToAction(nameof(Index));
        }

        var model = await _db.PageAsync(canonical, typeNames, page);

        if (model.Page != page)
        {
            // A stale or hand-typed page number lands on the nearest real page instead of an
            // error or a silently different page than the URL claims.
            return RedirectToAction(nameof(ByType), new { type = canonical, page = model.Page });
        }

        return View("Index", model);
    }
}
