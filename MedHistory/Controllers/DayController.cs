using System.Globalization;
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
        var entry = await _db.TickAsync(allocation, parsed);

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

        var entry = await _db.UntickAsync(tick.Value.EntryId);

        if (entry is not null)
        {
            _logger.LogInformation(
                "Allocation {AllocationId} slot {Slot} unticked, entry {EntryId} deleted",
                id, MedPlanRules.SlotName(parsed), entry.Id);
        }

        return RedirectToDay(allocation.Day);
    }

    [HttpPost("/day/{date}/weight")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddWeight(string date, string? time, string? value)
    {
        if (!AppTime.TryParseDay(date, out var day))
        {
            return RedirectToAction(nameof(Index));
        }

        if (!TimeOnly.TryParse(time, CultureInfo.InvariantCulture, out var timeOnly)
            || !MeasurementRules.TryParseValue(value, out var parsed))
        {
            TempData["WeightError"] = "Enter a valid time and weight.";
            return RedirectToDay(day);
        }

        await _db.AddWeightAsync(day, timeOnly, parsed);

        return RedirectToDay(day);
    }

    [HttpPost("/day/{date}/weight/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteWeight(string date, int id)
    {
        if (!AppTime.TryParseDay(date, out var day))
        {
            return RedirectToAction(nameof(Index));
        }

        await _db.DeleteWeightAsync(day, id);

        return RedirectToDay(day);
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
        var action = AnxietyRules.DecideVote(existing?.Level, requested);
        await _db.ApplyVoteAsync(day, existing, requested, action);

        return RedirectToDay(day);
    }

    private async Task<IActionResult> ShowDay(DateOnly day) =>
        View("Index", await _db.DayPageAsync(day));

    private IActionResult RedirectToDay(DateOnly day) =>
        RedirectToAction(nameof(ByDate), new { date = AppTime.Key(day) });
}
