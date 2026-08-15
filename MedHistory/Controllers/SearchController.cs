using MedHistory.Data;
using MedHistory.Models;
using MedHistory.Services;
using Microsoft.AspNetCore.Mvc;

namespace MedHistory.Controllers;

/// <summary>
/// Full-text-ish search over every entry's note and med name. A read-only cousin of the type
/// report: same distinct-day pagination, same two-query shape, but scoped by a substring match
/// across two columns instead of one type name, and mixing every type together on a matched day.
///
/// The query and view-model assembly live in <see cref="SearchQueries.PageAsync"/>; this
/// controller keeps the pure query normalisation and the one redirect decision search needs — an
/// out-of-range page, learned from the already-clamped <c>Page</c> the query returns.
/// </summary>
public class SearchController : Controller
{
    private readonly AppDbContext _db;

    public SearchController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("/search")]
    public async Task<IActionResult> Index([FromQuery] string? q, [FromQuery] int page = 1)
    {
        var query = SearchRules.NormalizeQuery(q);

        if (query is null)
        {
            // Bare form — no search has run yet, nothing to query for.
            return View("Index", new SearchViewModel { Query = null });
        }

        var model = await _db.PageAsync(query, page);

        if (model.Page != page)
        {
            // A stale or hand-typed page number lands on the nearest real page instead of an
            // error or a silently different page than the URL claims.
            return RedirectToAction(nameof(Index), new { q = query, page = model.Page });
        }

        return View("Index", model);
    }
}
