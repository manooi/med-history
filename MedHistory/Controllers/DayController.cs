using MedHistory.Data;
using MedHistory.Models;
using MedHistory.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Controllers;

/// <summary>
/// The day page and everything on it. Ticking or unticking a medication dose lives here
/// too — a tick is a real Pill <see cref="Entry"/>, so it belongs next to the entry actions
/// and always lands back on the day view where progress is shown. Adding, removing and
/// copying forward the day's allocations lives on its own page — see
/// <see cref="MedsController"/>.
/// </summary>
public class DayController : Controller
{
    private readonly AppDbContext _db;
    private readonly ILogger<DayController> _logger;

    public DayController(AppDbContext db, ILogger<DayController> logger)
    {
        _db = db;
        _logger = logger;
    }

    // GET / — today, via the default conventional route.
    public Task<IActionResult> Index() => ShowDay(AppTime.Today());

    [HttpGet("/day/{date}")]
    public Task<IActionResult> ByDate(string date)
    {
        if (!AppTime.TryParseDay(date, out var day))
        {
            return Task.FromResult<IActionResult>(RedirectToAction(nameof(Index)));
        }

        return ShowDay(day);
    }

    [HttpPost("/checklist/{id:int}/tick")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Tick(int id)
    {
        var allocation = await _db.MedAllocations.FindAsync(id);

        if (allocation is null)
        {
            return NotFound();
        }

        // Deliberately no active-type check: Pill is built-in and cannot be deleted, and a
        // checklist the user is working through must keep working even if the Pill type has
        // been deactivated on the /types page.
        var entry = new Entry
        {
            Type = BuiltInEntryTypes.Pill,
            PillName = allocation.Name,
            OccurredAt = AppTime.TickTime(allocation.Day)
        };

        _db.Entries.Add(entry);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Allocation {AllocationId} ticked, entry {EntryId} created", id, entry.Id);

        return RedirectToDay(allocation.Day);
    }

    [HttpPost("/checklist/{id:int}/untick")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Untick(int id)
    {
        var allocation = await _db.MedAllocations.FindAsync(id);

        if (allocation is null)
        {
            return NotFound();
        }

        var newest = ChecklistRules.NewestMatch(await PillLogs(allocation.Day), allocation.Name);

        if (newest is null)
        {
            // Nothing logged for this medication — the button is hidden in that state, so
            // this only happens on a double submit. Land back on the day either way.
            return RedirectToDay(allocation.Day);
        }

        var entry = await _db.Entries.FindAsync(newest.Value.EntryId);

        if (entry is not null)
        {
            // Photos go with it — the FK is ON DELETE CASCADE.
            _db.Entries.Remove(entry);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Allocation {AllocationId} unticked, entry {EntryId} deleted", id, entry.Id);
        }

        return RedirectToDay(allocation.Day);
    }

    private async Task<IActionResult> ShowDay(DateOnly day)
    {
        var (start, end) = AppTime.DayRange(day);

        // Projected rather than Include'd: the photo bytes are never needed here,
        // only the ids so the view can request thumbnails via GET /photos/{id}.
        var rows = await _db.Entries
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
                PhotoIds = e.Photos.Select(p => p.Id).ToList()
            })
            .ToListAsync();

        // The "+" buttons come from the types table, so adding a type needs no code change.
        // Deactivated types drop out here while their existing entries keep rendering.
        var activeTypes = await _db.EntryTypes
            .AsNoTracking()
            .Where(t => t.IsActive)
            .Select(t => t.Name)
            .ToListAsync();

        var allocations = await _db.MedAllocations
            .AsNoTracking()
            .Where(a => a.Day == day)
            .OrderBy(a => a.Id)
            .ToListAsync();

        // The day's entries are already loaded, so progress is counted in memory rather than
        // with a query per allocation.
        var pillLogs = rows
            .Where(r => ChecklistRules.IsPillEntry(r.Type))
            .Select(r => new PillLog(r.Id, r.PillName, r.OccurredAt));

        // OccurredAt ties get a deterministic secondary sort (type name, alphabetical)
        // rather than DB order — see EntryRules.OrderEntries.
        var ordered = EntryRules.OrderEntries(rows, r => r.OccurredAt, r => r.Type);

        var model = new DayViewModel
        {
            Day = day,
            IsToday = day == AppTime.Today(),
            NewEntryTypes = EntryTypeRules.SortForDisplay(activeTypes, name => name),
            Checklist = ChecklistRules.DeriveProgress(allocations, pillLogs),
            Entries = ordered.Select(r => new DayEntryViewModel
            {
                Id = r.Id,
                OccurredAtLocal = AppTime.ToLocal(r.OccurredAt),
                Type = r.Type,
                Detail = EntryRules.DetailLine(r.Type, r.Severity, r.PillName, r.Note),
                PhotoIds = r.PhotoIds
            }).ToList()
        };

        return View("Index", model);
    }

    /// <summary>The day's Pill entries — the raw material every checklist count is derived from.</summary>
    private async Task<List<PillLog>> PillLogs(DateOnly day)
    {
        var (start, end) = AppTime.DayRange(day);

        // The type filter is the SQL twin of ChecklistRules.IsPillEntry — same ordinal
        // comparison against the same constant, written inline because EF cannot translate
        // a method call into SQL. ChecklistRulesTests pins the two to the same meaning.
        var rows = await _db.Entries
            .AsNoTracking()
            .Where(e => e.OccurredAt >= start && e.OccurredAt < end && e.Type == BuiltInEntryTypes.Pill)
            .Select(e => new { e.Id, e.PillName, e.OccurredAt })
            .ToListAsync();

        return rows.Select(r => new PillLog(r.Id, r.PillName, r.OccurredAt)).ToList();
    }

    private IActionResult RedirectToDay(DateOnly day) =>
        RedirectToAction(nameof(ByDate), new { date = AppTime.Key(day) });
}
