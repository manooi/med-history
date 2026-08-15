using MedHistory.Data;
using MedHistory.Models;
using MedHistory.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Controllers;

/// <summary>
/// Read-only history of one entry type across every day it was ever logged, newest day first.
/// A mirror image of the day page: that page is every type on one day, this page is one type
/// across every day. Nothing here writes anything — every row still links back to
/// <c>/entries/{id}/edit</c>, which is where an entry is actually changed.
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
            AllTypeNames = await AllTypeNamesAsync(),
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
        var typeNames = await AllTypeNamesAsync();

        // Matched case-insensitively, like every other type lookup in the app, but the entries
        // themselves are queried by the stored row's own casing — Entry.Type is compared
        // ordinal, and a hand-typed URL must not be able to miss a type by casing alone.
        var canonical = typeNames.FirstOrDefault(name => EntryTypeRules.NamesMatch(name, type));

        if (canonical is null)
        {
            return RedirectToAction(nameof(Index));
        }

        // Every instant this type was ever logged at. Deliberately just the timestamp: which
        // local day each one falls on is all that decides the page layout, and reading the rest
        // of the row for every entry the type has ever had — most of which will not even be on
        // this page — would be the N+1 this design avoids.
        var instants = await _db.Entries
            .AsNoTracking()
            .Where(e => e.Type == canonical)
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
            return RedirectToAction(nameof(ByType), new { type = canonical, page = clampedPage });
        }

        var window = TypeReportRules.SelectDays(distinctDays, clampedPage);

        var model = new TypeReportViewModel
        {
            AllTypeNames = typeNames,
            CurrentType = canonical,
            Days = window.Count == 0 ? [] : await LoadDaysAsync(canonical, window),
            Page = clampedPage,
            PageCount = pageCount
        };

        return View("Index", model);
    }

    /// <summary>
    /// The page's entries, grouped by day. One query: the window is a contiguous slice of the
    /// type's own distinct days, so the instant range from the oldest day's start to the newest
    /// day's end holds exactly these days' entries of this type and nothing outside the window —
    /// any calendar day inside that range but outside the distinct-day set has no entry of this
    /// type to begin with, or it would already be in that set.
    /// </summary>
    private async Task<IReadOnlyList<TypeReportDayViewModel>> LoadDaysAsync(string type, IReadOnlyList<DateOnly> window)
    {
        var newest = window[0];
        var oldest = window[^1];
        var (start, _) = AppTime.DayRange(oldest);
        var (_, end) = AppTime.DayRange(newest);

        // Projected rather than Include'd: the photo bytes are never needed here, only the ids
        // so the view can request thumbnails via GET /photos/{id} — same shape as the day page.
        var rows = await _db.Entries
            .AsNoTracking()
            .Where(e => e.Type == type && e.OccurredAt >= start && e.OccurredAt < end)
            .Select(e => new
            {
                e.Id,
                e.OccurredAt,
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
                Type = type,
                Detail = EntryRules.DetailLine(type, r.Severity, r.PillName, r.Note),
                PhotoIds = r.PhotoIds
            }).ToList()
        }).ToList();
    }

    /// <summary>Every type name, in the same order the /types page lists them — the selector row.</summary>
    private async Task<IReadOnlyList<string>> AllTypeNamesAsync()
    {
        var names = await _db.EntryTypes.AsNoTracking().Select(t => t.Name).ToListAsync();
        return EntryTypeRules.SortForDisplay(names, name => name);
    }
}
