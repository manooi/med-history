using MedHistory.Data;
using MedHistory.Models;
using MedHistory.Services;
using Microsoft.AspNetCore.Mvc;

namespace MedHistory.Controllers;

/// <summary>
/// Read-only history of any set of entry types across every day one of them was logged, newest
/// day first. A mirror image of the day page: that page is every type on one day, this page is a
/// chosen set of types across every day. Nothing here writes anything — every row still links
/// back to <c>/entries/{id}/edit</c>, which is where an entry is actually changed.
///
/// The selection rides as a repeated <c>types</c> query parameter, never a joined path segment:
/// type names are user free text, so there is no separator character a name could not contain.
///
/// The queries and view-model assembly live in <see cref="TypeReportQueries"/>; this controller
/// keeps the two decisions that depend on what a query finds — a selection asked for in any
/// spelling but its canonical one (resolved here, since
/// <see cref="TypeReportQueries.AllTypeNamesAsync"/> is cheap and the redirect needs it before
/// anything else runs) redirects to that canonical URL, and an out-of-range page (learned from
/// <see cref="TypeReportQueries.PageAsync"/>'s already-clamped <c>Page</c>) redirects to the
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
    public async Task<IActionResult> Index([FromQuery(Name = "types")] string[]? types, [FromQuery] int page = 1)
    {
        var typeNames = await _db.AllTypeNamesAsync();
        var requested = types ?? [];
        var selected = TypeReportRules.CanonicalizeTypes(requested, typeNames);

        if (TypeReportRules.NeedsCanonicalRedirect(requested, selected))
        {
            // Unknown names dropped, duplicates collapsed, casing and order normalised — sent
            // before any entries are read, so the URL and the page always agree on the selection.
            return Redirect(TypeReportRules.Href(selected, page));
        }

        if (selected.Count == 0)
        {
            // The bare selector page: nothing ticked, so there is nothing to page through.
            return View("Index", new TypeReportViewModel
            {
                AllTypeNames = typeNames,
                SelectedTypes = selected,
                Days = [],
                Page = 1,
                PageCount = 0
            });
        }

        var model = await _db.PageAsync(selected, typeNames, page);

        if (model.Page != page)
        {
            // A stale or hand-typed page number lands on the nearest real page instead of an
            // error or a silently different page than the URL claims.
            return Redirect(TypeReportRules.Href(selected, model.Page));
        }

        return View("Index", model);
    }

    /// <summary>
    /// The single-type URL the report used to live at, kept so old bookmarks and links still
    /// land somewhere. It only rewrites the address — <see cref="Index"/> does the matching, so
    /// a type that has since been renamed away falls out there as an unknown name.
    /// </summary>
    [HttpGet("/type-report/{type}")]
    public IActionResult ByType(string type, [FromQuery] int page = 1) =>
        Redirect(TypeReportRules.Href([type], page));
}
