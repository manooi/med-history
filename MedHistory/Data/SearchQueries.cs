using MedHistory.Models;
using MedHistory.Services;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Data;

/// <summary>
/// The database side of search: a substring scan of every entry's note and med name, paged the
/// same way <see cref="TypeReportQueries"/> pages a type's history. Lives here so
/// <see cref="MedHistory.Controllers.SearchController"/> keeps only the pure query normalisation
/// and the page-clamp redirect. Unlike the type report there is no "unknown query" redirect — any
/// normalized query is valid — so the only decision <see cref="PageAsync"/> needs to expose is the
/// clamped page, returned already corrected on <c>Page</c>, with the entries query skipped when
/// the caller's page will turn out to need a redirect, same short-circuit as before.
/// </summary>
public static class SearchQueries
{
    public static async Task<SearchViewModel> PageAsync(this AppDbContext db, string query, int page)
    {
        var pattern = $"%{SearchRules.EscapeLike(query)}%";

        // Instant-only scan of matching entries, same shape as the type report's first query:
        // which local day each match falls on is all that's needed to lay out the page.
        var instants = await db.Entries
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

        IReadOnlyList<TypeReportDayViewModel> days = [];

        if (clampedPage == page)
        {
            var window = TypeReportRules.SelectDays(distinctDays, clampedPage);
            days = window.Count == 0 ? [] : await LoadDaysAsync(db, pattern, window);
        }

        return new SearchViewModel
        {
            Query = query,
            MatchedDayCount = distinctDays.Count,
            Days = days,
            Page = clampedPage,
            PageCount = pageCount
        };
    }

    /// <summary>
    /// The page's matching entries, grouped by day. One query, same reasoning as the type
    /// report's: the window is a contiguous slice of the query's own distinct match-days, so the
    /// instant range from the oldest day's start to the newest day's end holds exactly these
    /// days' matching entries and nothing outside the window. Unlike the type report, the match
    /// filter (both columns) has to run again here — the day window alone would also pull in
    /// that day's non-matching entries of other types.
    /// </summary>
    private static async Task<IReadOnlyList<TypeReportDayViewModel>> LoadDaysAsync(
        AppDbContext db, string pattern, IReadOnlyList<DateOnly> window)
    {
        var newest = window[0];
        var oldest = window[^1];
        var (start, _) = AppTime.DayRange(oldest);
        var (_, end) = AppTime.DayRange(newest);

        // Projected rather than Include'd: the photo bytes are never needed here, only the ids
        // so the view can request thumbnails via GET /photos/{id} — same shape as the type report.
        var rows = await db.Entries
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
