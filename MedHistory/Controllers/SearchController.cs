using MedHistory.Data;
using MedHistory.Models;
using MedHistory.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Controllers;

/// <summary>
/// Full-text-ish search over every entry's note and med name. A read-only cousin of the type
/// report: same distinct-day pagination, same two-query shape, but scoped by a substring match
/// across two columns instead of one type name, and mixing every type together on a matched day.
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

        var pattern = $"%{SearchRules.EscapeLike(query)}%";

        // Instant-only scan of matching entries, same shape as the type report's first query:
        // which local day each match falls on is all that's needed to lay out the page.
        var instants = await _db.Entries
            .AsNoTracking()
            .Where(e => EF.Functions.ILike(e.Note!, pattern, "\\") || EF.Functions.ILike(e.PillName!, pattern, "\\"))
            .Select(e => e.OccurredAt)
            .ToListAsync();

        var distinctDays = instants
            .Select(AppTime.DayOf)
            .Distinct()
            .OrderByDescending(day => day)
            .ToList();

        var pageCount = TypeReportRules.PageCount(distinctDays.Count);
        var clampedPage = TypeReportRules.ClampPage(page, pageCount);

        if (clampedPage != page)
        {
            // A stale or hand-typed page number lands on the nearest real page instead of an
            // error or a silently different page than the URL claims.
            return RedirectToAction(nameof(Index), new { q = query, page = clampedPage });
        }

        var window = TypeReportRules.SelectDays(distinctDays, clampedPage);

        var model = new SearchViewModel
        {
            Query = query,
            MatchedDayCount = distinctDays.Count,
            Days = window.Count == 0 ? [] : await LoadDaysAsync(pattern, window),
            Page = clampedPage,
            PageCount = pageCount
        };

        return View("Index", model);
    }

    /// <summary>
    /// The page's matching entries, grouped by day. One query, same reasoning as the type
    /// report's: the window is a contiguous slice of the query's own distinct match-days, so the
    /// instant range from the oldest day's start to the newest day's end holds exactly these
    /// days' matching entries and nothing outside the window. Unlike the type report, the match
    /// filter (both columns) has to run again here — the day window alone would also pull in
    /// that day's non-matching entries of other types.
    /// </summary>
    private async Task<IReadOnlyList<TypeReportDayViewModel>> LoadDaysAsync(string pattern, IReadOnlyList<DateOnly> window)
    {
        var newest = window[0];
        var oldest = window[^1];
        var (start, _) = AppTime.DayRange(oldest);
        var (_, end) = AppTime.DayRange(newest);

        // Projected rather than Include'd: the photo bytes are never needed here, only the ids
        // so the view can request thumbnails via GET /photos/{id} — same shape as the type report.
        var rows = await _db.Entries
            .AsNoTracking()
            .Where(e => e.OccurredAt >= start && e.OccurredAt < end
                && (EF.Functions.ILike(e.Note!, pattern, "\\") || EF.Functions.ILike(e.PillName!, pattern, "\\")))
            .Select(e => new
            {
                e.Id,
                e.OccurredAt,
                e.Type,
                e.Severity,
                e.PillName,
                e.Note,
                PhotoIds = e.Photos.Select(p => p.Id).ToList()
            })
            .ToListAsync();

        var groups = TypeReportRules.GroupByDayDescending(rows, r => AppTime.DayOf(r.OccurredAt), r => r.OccurredAt);

        return groups.Select(group => new TypeReportDayViewModel
        {
            Day = group.Key,
            Entries = group.Select(r => new DayEntryViewModel
            {
                Id = r.Id,
                OccurredAtLocal = AppTime.ToLocal(r.OccurredAt),
                Type = r.Type,
                Detail = EntryRules.DetailLine(r.Type, r.Severity, r.PillName, r.Note),
                PhotoIds = r.PhotoIds
            }).ToList()
        }).ToList();
    }
}
