using MedHistory.Data;
using MedHistory.Models;
using MedHistory.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Controllers;

public class EntriesController : Controller
{
    private readonly AppDbContext _db;
    private readonly ILogger<EntriesController> _logger;

    public EntriesController(AppDbContext db, ILogger<EntriesController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet("/entries/new")]
    public async Task<IActionResult> New(string? type, string? date)
    {
        var (availability, canonicalType) = await ResolveType(type);

        if (availability != TypeAvailability.Ok || canonicalType is null)
        {
            return BadRequest();
        }

        var day = AppTime.TryParseDay(date, out var parsed) ? parsed : AppTime.Today();

        return View("Form", new EntryFormViewModel
        {
            Type = canonicalType,
            OccurredAt = day.ToDateTime(TimeOnly.FromDateTime(DateTime.Now), DateTimeKind.Unspecified),
            Severity = EntryRules.RequiresSeverity(canonicalType) ? Models.Severity.Light : null
        });
    }

    [HttpPost("/entries")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(60 * 1024 * 1024)]
    public async Task<IActionResult> Create(EntryFormViewModel form, List<IFormFile>? photos)
    {
        // The type arrives in a hidden field, so it is re-checked here: it must still
        // exist and still be active before an entry can be filed under it.
        var (availability, canonicalType) = await ResolveType(form.Type);

        if (availability != TypeAvailability.Ok || canonicalType is null)
        {
            return BadRequest();
        }

        form.Id = null;
        form.Type = canonicalType;
        ValidateForm(form, photos);

        if (!ModelState.IsValid)
        {
            return View("Form", form);
        }

        var entry = new Entry { Type = form.Type };
        EntryRules.CopyInto(entry, form);
        await entry.AttachPhotosAsync(photos);

        _db.Entries.Add(entry);
        await _db.SaveChangesAsync();

        // Ids and types only — notes and photo bytes stay out of the log.
        _logger.LogInformation("Entry {EntryId} created, type {EntryType}", entry.Id, entry.Type);
        LogPhotosAttached(entry.Id, photos);

        return RedirectToDay(AppTime.DayOf(entry.OccurredAt));
    }

    [HttpGet("/entries/{id:int}/edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var entry = await _db.Entries.FindAsync(id);

        if (entry is null)
        {
            return NotFound();
        }

        return View("Form", new EntryFormViewModel
        {
            Id = entry.Id,
            Type = entry.Type,
            OccurredAt = AppTime.ToLocal(entry.OccurredAt).DateTime,
            Note = entry.Note,
            Severity = entry.Severity,
            PillName = entry.PillName,
            ExistingPhotos = await _db.LoadPhotoSummariesAsync(id)
        });
    }

    [HttpPost("/entries/{id:int}")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(60 * 1024 * 1024)]
    public async Task<IActionResult> Update(int id, EntryFormViewModel form, List<IFormFile>? photos)
    {
        var entry = await _db.Entries.FindAsync(id);

        if (entry is null)
        {
            return NotFound();
        }

        // Type is fixed at creation; the posted value is never trusted. Taking it from
        // the stored row is also what keeps an entry editable after its type has been
        // deactivated — the active-type check above deliberately does not run here.
        form.Id = entry.Id;
        form.Type = entry.Type;
        ValidateForm(form, photos);

        if (!ModelState.IsValid)
        {
            form.ExistingPhotos = await _db.LoadPhotoSummariesAsync(id);
            return View("Form", form);
        }

        EntryRules.CopyInto(entry, form);
        await entry.AttachPhotosAsync(photos);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Entry {EntryId} updated, type {EntryType}", entry.Id, entry.Type);
        LogPhotosAttached(entry.Id, photos);

        return RedirectToDay(AppTime.DayOf(entry.OccurredAt));
    }

    [HttpPost("/entries/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var entry = await _db.Entries.FindAsync(id);

        if (entry is null)
        {
            return NotFound();
        }

        // Photos go with it — the FK is ON DELETE CASCADE.
        var day = AppTime.DayOf(entry.OccurredAt);
        var type = entry.Type;
        _db.Entries.Remove(entry);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Entry {EntryId} deleted, type {EntryType}", id, type);

        return RedirectToDay(day);
    }

    /// <summary>
    /// Looks a posted type name up against the types table. The name is returned in its
    /// stored casing so entries are never filed under a variant the user typed into the URL.
    /// </summary>
    private async Task<(TypeAvailability Availability, string? CanonicalName)> ResolveType(string? name)
    {
        var types = await _db.EntryTypes
            .AsNoTracking()
            .Select(t => new { t.Name, t.IsActive })
            .ToListAsync();

        var availability = EntryTypeRules.CheckAvailable(name, types.Select(t => (t.Name, t.IsActive)));
        var canonical = types.FirstOrDefault(t => EntryTypeRules.NamesMatch(t.Name, name))?.Name;

        return (availability, canonical);
    }

    private void ValidateForm(EntryFormViewModel form, List<IFormFile>? photos)
    {
        var occurredAtError = EntryRules.ValidateOccurredAt(form.OccurredAt);

        if (occurredAtError is not null)
        {
            ModelState.AddModelError(nameof(EntryFormViewModel.OccurredAt), occurredAtError);
        }

        foreach (var error in EntryRules.Validate(form.Type, form.Severity, form.PillName, form.Note))
        {
            ModelState.AddModelError(string.Empty, error);
        }

        if (photos is null)
        {
            return;
        }

        foreach (var photo in photos)
        {
            foreach (var error in PhotoRules.Validate(photo.ContentType, photo.Length))
            {
                ModelState.AddModelError(string.Empty, error);
            }
        }
    }

    private void LogPhotosAttached(int entryId, List<IFormFile>? photos)
    {
        if (photos is { Count: > 0 })
        {
            _logger.LogInformation("Entry {EntryId} gained {PhotoCount} photo(s)", entryId, photos.Count);
        }
    }

    private IActionResult RedirectToDay(DateOnly day) =>
        RedirectToAction(nameof(DayController.ByDate), "Day", new { date = AppTime.Key(day) });
}
