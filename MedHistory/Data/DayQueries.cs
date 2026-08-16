using MedHistory.Models;
using MedHistory.Services;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Data;

/// <summary>
/// Assembles the day page's view model for <see cref="MedHistory.Controllers.DayController"/> —
/// the entries, checklist, anxiety vote and new-entry-type buttons that make up a single day.
/// </summary>
public static class DayQueries
{
    public static async Task<DayViewModel> DayPageAsync(this AppDbContext db, DateOnly day)
    {
        var (start, end) = AppTime.DayRange(day);

        // Projected rather than Include'd: the photo bytes are never needed here,
        // only the ids so the view can request thumbnails via GET /photos/{id}.
        var rows = await db.Entries
            .Where(e => e.OccurredAt >= start && e.OccurredAt < end)
            .OrderBy(e => e.OccurredAt)
            .Select(e => new
            {
                e.Id,
                e.OccurredAt,
                e.Type,
                e.Note,
                e.Severity,
                e.PillName,
                e.ChecklistAllocationId,
                e.ChecklistSlot,
                PhotoIds = e.Photos.Select(p => p.Id).ToList()
            })
            .ToListAsync();

        // The "+" buttons come from the types table, so adding a type needs no code change.
        // Deactivated types drop out here while their existing entries keep rendering.
        var activeTypes = await db.EntryTypes
            .AsNoTracking()
            .Where(t => t.IsActive)
            .Select(t => t.Name)
            .ToListAsync();

        var allocations = await db.MedAllocations
            .AsNoTracking()
            .Where(a => a.Day == day)
            .OrderBy(a => a.Id)
            .ToListAsync();

        var vote = await db.AnxietyVotes
            .AsNoTracking()
            .Where(v => v.Day == day)
            .Select(v => (AnxietyLevel?)v.Level)
            .SingleOrDefaultAsync();

        var weightRows = await db.Measurements
            .AsNoTracking()
            .Where(m => m.Kind == MeasurementKinds.Weight && m.OccurredAt >= start && m.OccurredAt < end)
            .OrderBy(m => m.OccurredAt)
            .Select(m => new { m.Id, m.OccurredAt, m.Value })
            .ToListAsync();

        // The day's entries are already loaded, so which slots are ticked is worked out in
        // memory rather than with a query per allocation.
        var ticks = rows
            .Where(r => r.ChecklistAllocationId is not null)
            .Select(r => new ChecklistTick(r.Id, r.ChecklistAllocationId, r.ChecklistSlot));

        // What is left of each medication counts every dose ever logged, not this day's — so it
        // cannot come off the rows above. Skipped entirely on a day with nothing planned, which
        // has no row for a count to appear on.
        IReadOnlyList<MedStockRow> stock = allocations.Count == 0 ? [] : await db.StockRowsAsync();

        // OccurredAt ties get a deterministic secondary sort (type name, alphabetical)
        // rather than DB order — see EntryRules.OrderEntries.
        var ordered = EntryRules.OrderEntries(rows, r => r.OccurredAt, r => r.Type);

        return new DayViewModel
        {
            Day = day,
            IsToday = day == AppTime.Today(),
            NewEntryTypes = EntryTypeRules.SortForDisplay(activeTypes, name => name),
            Checklist = ChecklistRules.DeriveRows(allocations, ticks, stock),
            AnxietyLevel = vote,
            WeightMeasurements = weightRows.Select(r => new WeightMeasurementViewModel
            {
                Id = r.Id,
                OccurredAtLocal = AppTime.ToLocal(r.OccurredAt),
                Value = r.Value
            }).ToList(),
            Entries = ordered.Select(r => new DayEntryViewModel
            {
                Id = r.Id,
                OccurredAtLocal = AppTime.ToLocal(r.OccurredAt),
                Type = r.Type,
                Detail = EntryRules.DetailLine(r.Type, r.Severity, r.PillName, r.Note),
                PhotoIds = r.PhotoIds
            }).ToList()
        };
    }
}
