using MedHistory.Data;
using MedHistory.Models;
using MedHistory.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Controllers;

/// <summary>
/// The day page and everything on it. Ticking or unticking a medication dose lives here
/// too — a tick is a real Med <see cref="Entry"/>, so it belongs next to the entry actions
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

    [HttpPost("/checklist/{id:int}/tick/{slot}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Tick(int id, string slot)
    {
        var (allocation, parsed) = await _db.ResolveSlotAsync(id, slot);

        if (allocation is null)
        {
            return NotFound();
        }

        // A slot is ticked exactly when a linked entry exists, so ticking one that already has
        // an entry must add nothing: a second entry would leave the slot ticked after an untick
        // removed only one of them. A double submit therefore just lands back on the day.
        if (ChecklistRules.FindTick(await _db.TicksAsync(allocation.Day), id, parsed) is not null)
        {
            return RedirectToDay(allocation.Day);
        }

        // Deliberately no active-type check: Med is built-in and cannot be deleted, and a
        // checklist the user is working through must keep working even if the Med type has
        // been deactivated on the /types page.
        var entry = new Entry
        {
            Type = BuiltInEntryTypes.Med,
            PillName = allocation.Name,
            OccurredAt = AppTime.TickTime(allocation.Day, parsed),
            ChecklistAllocationId = allocation.Id,
            ChecklistSlot = MedPlanRules.SlotName(parsed),
            // Stamped, not looked up: this is what the dose was, and a later edit to the plan
            // must not rewrite it — see Entry.DoseQuantity.
            DoseQuantity = allocation.DoseQuantity,
            // Stamped for the same reason, and the one that makes renaming safe: the dose stays
            // counted against the stock it actually came out of however that stock or this plan
            // is renamed afterwards. Null when nothing stocks the medication, which leaves the
            // dose counting by name like any hand-typed one — see Entry.MedStockId.
            MedStockId = allocation.MedStockId,
            // The timeline shows the note as typed, so the slot, the dose and how it is taken
            // are written into it — otherwise a ticked dose reads as a bare medication name.
            Note = MedPlanRules.ComposeNote(
                parsed, allocation.MealRelation, allocation.Method, allocation.DoseQuantity)
        };

        _db.Entries.Add(entry);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Allocation {AllocationId} slot {Slot} ticked, entry {EntryId} created",
            id, entry.ChecklistSlot, entry.Id);

        return RedirectToDay(allocation.Day);
    }

    [HttpPost("/checklist/{id:int}/untick/{slot}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Untick(int id, string slot)
    {
        var (allocation, parsed) = await _db.ResolveSlotAsync(id, slot);

        if (allocation is null)
        {
            return NotFound();
        }

        var tick = ChecklistRules.FindTick(await _db.TicksAsync(allocation.Day), id, parsed);

        if (tick is null)
        {
            // Nothing ticked this slot — the untick control is only drawn when something did,
            // so this is a double submit. Land back on the day either way.
            return RedirectToDay(allocation.Day);
        }

        var entry = await _db.Entries.FindAsync(tick.Value.EntryId);

        if (entry is not null)
        {
            // Photos go with it — the FK is ON DELETE CASCADE.
            _db.Entries.Remove(entry);
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Allocation {AllocationId} slot {Slot} unticked, entry {EntryId} deleted",
                id, MedPlanRules.SlotName(parsed), entry.Id);
        }

        return RedirectToDay(allocation.Day);
    }

    [HttpPost("/day/{date}/anxiety/{level}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Vote(string date, string level)
    {
        if (!AppTime.TryParseDay(date, out var day))
        {
            return RedirectToAction(nameof(Index));
        }

        if (!AnxietyRules.TryParseLevel(level, out var requested))
        {
            return NotFound();
        }

        var existing = await _db.AnxietyVotes.SingleOrDefaultAsync(v => v.Day == day);

        // Voting the level already set clears the day — see AnxietyRules.DecideVote, which is
        // what turns a second tap of the same button into the day widget's only undo control.
        if (AnxietyRules.DecideVote(existing?.Level, requested) == VoteAction.Clear)
        {
            _db.AnxietyVotes.Remove(existing!);
        }
        else if (existing is null)
        {
            _db.AnxietyVotes.Add(new AnxietyVote { Day = day, Level = requested });
        }
        else
        {
            existing.Level = requested;
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // The unique index on Day is the real guard; the check above only beats it if two
            // votes for the same not-yet-voted day race, which is worth losing quietly rather
            // than a 500 — the winning request already recorded the vote, and PRG makes the
            // loser's redirect harmless. Same pattern as StocksController.AddStock.
            return RedirectToDay(day);
        }

        return RedirectToDay(day);
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
                e.ChecklistAllocationId,
                e.ChecklistSlot,
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

        var vote = await _db.AnxietyVotes
            .AsNoTracking()
            .Where(v => v.Day == day)
            .Select(v => (AnxietyLevel?)v.Level)
            .SingleOrDefaultAsync();

        // The day's entries are already loaded, so which slots are ticked is worked out in
        // memory rather than with a query per allocation.
        var ticks = rows
            .Where(r => r.ChecklistAllocationId is not null)
            .Select(r => new ChecklistTick(r.Id, r.ChecklistAllocationId, r.ChecklistSlot));

        // What is left of each medication counts every dose ever logged, not this day's — so it
        // cannot come off the rows above. Skipped entirely on a day with nothing planned, which
        // has no row for a count to appear on.
        IReadOnlyList<MedStockRow> stock = allocations.Count == 0 ? [] : await _db.StockRowsAsync();

        // OccurredAt ties get a deterministic secondary sort (type name, alphabetical)
        // rather than DB order — see EntryRules.OrderEntries.
        var ordered = EntryRules.OrderEntries(rows, r => r.OccurredAt, r => r.Type);

        var model = new DayViewModel
        {
            Day = day,
            IsToday = day == AppTime.Today(),
            NewEntryTypes = EntryTypeRules.SortForDisplay(activeTypes, name => name),
            Checklist = ChecklistRules.DeriveRows(allocations, ticks, stock),
            AnxietyLevel = vote,
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

    private IActionResult RedirectToDay(DateOnly day) =>
        RedirectToAction(nameof(ByDate), new { date = AppTime.Key(day) });
}
