using MedHistory.Data;
using MedHistory.Models;
using MedHistory.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Controllers;

/// <summary>
/// The day page and everything on it, including the medication checklist. The checklist
/// actions live here rather than in a controller of their own because a rejected add has
/// to re-render the day — sharing <see cref="ShowDay"/> is what keeps that from duplicating
/// the page's queries.
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

    [HttpPost("/day/{date}/checklist")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddAllocation(string date, string? name, int requiredCount = ChecklistRules.MinRequiredCount)
    {
        if (!AppTime.TryParseDay(date, out var day))
        {
            return RedirectToAction(nameof(Index));
        }

        var existingNames = await AllocationNames(day);

        foreach (var error in ChecklistRules.ValidateNewAllocation(name, requiredCount, existingNames))
        {
            ModelState.AddModelError(string.Empty, error);
        }

        if (!ModelState.IsValid)
        {
            return await ShowDay(day, name, requiredCount);
        }

        // Non-null: ValidateNewAllocation rejects a name that normalises away.
        var allocation = new MedAllocation
        {
            Day = day,
            Name = ChecklistRules.NormalizeName(name)!,
            RequiredCount = requiredCount
        };

        _db.MedAllocations.Add(allocation);
        await _db.SaveChangesAsync();

        // Ids and counts only — a medication name is health data and stays out of the log,
        // the same way entry notes do.
        _logger.LogInformation(
            "Allocation {AllocationId} added for {Day}, {RequiredCount} per day",
            allocation.Id, AppTime.Key(day), allocation.RequiredCount);

        return RedirectToDay(day);
    }

    [HttpPost("/day/{date}/checklist/copy-previous")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CopyPreviousDay(string date)
    {
        if (!AppTime.TryParseDay(date, out var day))
        {
            return RedirectToAction(nameof(Index));
        }

        var previous = day.AddDays(-1);
        var source = await _db.MedAllocations.AsNoTracking().Where(a => a.Day == previous).OrderBy(a => a.Id).ToListAsync();
        var copied = ChecklistRules.AllocationsToCopy(source, await AllocationNames(day));

        // Allocations only: the copies start at 0/N however much of the previous day was ticked.
        foreach (var allocation in copied)
        {
            _db.MedAllocations.Add(new MedAllocation
            {
                Day = day,
                Name = allocation.Name,
                RequiredCount = allocation.RequiredCount
            });
        }

        if (copied.Count > 0)
        {
            await _db.SaveChangesAsync();
        }

        _logger.LogInformation("Copied {Count} allocation(s) from {Previous} to {Day}",
            copied.Count, AppTime.Key(previous), AppTime.Key(day));

        return RedirectToDay(day);
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

    [HttpPost("/checklist/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveAllocation(int id)
    {
        var allocation = await _db.MedAllocations.FindAsync(id);

        if (allocation is null)
        {
            return NotFound();
        }

        // The row only. Pill entries logged against this medication are the day's record of
        // what was taken and are never touched by removing the plan for it.
        var day = allocation.Day;
        _db.MedAllocations.Remove(allocation);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Allocation {AllocationId} removed from {Day}", id, AppTime.Key(day));

        return RedirectToDay(day);
    }

    private async Task<IActionResult> ShowDay(DateOnly day, string? newMedName = null, int? newMedRequiredCount = null)
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
            CanCopyPreviousDay = await _db.MedAllocations.AnyAsync(a => a.Day == day.AddDays(-1)),
            NewMedName = newMedName,
            NewMedRequiredCount = newMedRequiredCount ?? ChecklistRules.MinRequiredCount,
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

    private async Task<List<string>> AllocationNames(DateOnly day) =>
        await _db.MedAllocations.AsNoTracking().Where(a => a.Day == day).Select(a => a.Name).ToListAsync();

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
