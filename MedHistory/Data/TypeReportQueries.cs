using MedHistory.Models;
using MedHistory.Services;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Data;

/// <summary>
/// The database side of the type report: the selector's type names, and the selected types'
/// entries merged into one timeline grouped by day, newest first, paged in blocks of whole days.
/// Lives here so <see cref="MedHistory.Controllers.TypeReportController"/> keeps only route
/// parsing and the redirect decisions that depend on what a query finds — a non-canonical
/// selection (resolved against <see cref="AllTypeNamesAsync"/>, itself cheap enough to run before
/// deciding whether to query further) and an out-of-range page. <see cref="PageAsync"/> reports the second by paging
/// exactly as before: it clamps and returns <c>Page</c> already corrected, and skips the entries
/// query entirely when the caller's page will turn out to need a redirect — same two-query
/// short-circuit the controller used to do inline, just relocated. The controller compares its
/// requested page against the model's <c>Page</c> to know whether to redirect.
/// </summary>
public static class TypeReportQueries
{
    /// <summary>Every type name, in the same order the /types page lists them — the selector row.</summary>
    public static async Task<IReadOnlyList<string>> AllTypeNamesAsync(this AppDbContext db)
    {
        var names = await db.EntryTypes.AsNoTracking().Select(t => t.Name).ToListAsync();
        return EntryTypeRules.SortForDisplay(names, name => name);
    }

    public static async Task<TypeReportViewModel> PageAsync(
        this AppDbContext db, IReadOnlyList<string> canonicalTypes, IReadOnlyList<string> allTypeNames, int page)
    {
        // Materialised as a List so EF translates the membership test to a plain SQL IN.
        var types = canonicalTypes.ToList();

        // Every instant any of these types was ever logged at. Deliberately just the timestamp:
        // which local day each one falls on is all that decides the page layout, and reading the
        // rest of the row for every entry the selection has ever had — most of which will not even
        // be on this page — would be the N+1 this design avoids.
        var instants = await db.Entries
            .AsNoTracking()
            .Where(e => types.Contains(e.Type))
            .Select(e => e.OccurredAt)
            .ToListAsync();

        var distinctDays = instants
            .Select(AppTime.DayOf)
            .Distinct()
            .OrderByDescending(day => day)
            .ToList();

        var pageCount = TypeReportRules.PageCount(distinctDays.Count);
        var clampedPage = TypeReportRules.ClampPage(page, pageCount);

        // A stale or hand-typed page number needs the caller to redirect to the nearest real
        // page rather than render it — so the entries query is skipped here exactly as the
        // controller used to skip it before redirecting, and Days comes back empty.
        IReadOnlyList<TypeReportDayViewModel> days = [];

        if (clampedPage == page)
        {
            var window = TypeReportRules.SelectDays(distinctDays, clampedPage);
            days = window.Count == 0 ? [] : await LoadDaysAsync(db, types, window);
        }

        return new TypeReportViewModel
        {
            AllTypeNames = allTypeNames,
            SelectedTypes = canonicalTypes,
            Days = days,
            Page = clampedPage,
            PageCount = pageCount
        };
    }

    /// <summary>
    /// The page's entries, grouped by day. One query: the window is a contiguous slice of the
    /// selection's own distinct days, so the instant range from the oldest day's start to the
    /// newest day's end holds exactly these days' entries of these types and nothing outside the
    /// window — any calendar day inside that range but outside the distinct-day set has no entry
    /// of any selected type to begin with, or it would already be in that set.
    /// </summary>
    private static async Task<IReadOnlyList<TypeReportDayViewModel>> LoadDaysAsync(
        AppDbContext db, List<string> types, IReadOnlyList<DateOnly> window)
    {
        var newest = window[0];
        var oldest = window[^1];
        var (start, _) = AppTime.DayRange(oldest);
        var (_, end) = AppTime.DayRange(newest);

        // Projected rather than Include'd: the photo bytes are never needed here, only the ids
        // so the view can request thumbnails via GET /photos/{id} — same shape as the day page.
        var rows = await db.Entries
            .AsNoTracking()
            .Where(e => types.Contains(e.Type) && e.OccurredAt >= start && e.OccurredAt < end)
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

        var groups = TypeReportRules.GroupByDayDescending(
            rows, r => AppTime.DayOf(r.OccurredAt), r => r.OccurredAt, r => r.Type);

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
