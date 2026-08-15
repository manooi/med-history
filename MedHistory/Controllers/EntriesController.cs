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
        ApplyRules(form);
        ValidatePhotos(photos);

        if (!ModelState.IsValid)
        {
            return View("Form", form);
        }

        var entry = new Entry { Type = form.Type };
        CopyInto(entry, form);
        await AttachPhotos(entry, photos);

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
            ExistingPhotos = await LoadPhotoSummaries(id)
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
        ApplyRules(form);
        ValidatePhotos(photos);

        if (!ModelState.IsValid)
        {
            form.ExistingPhotos = await LoadPhotoSummaries(id);
            return View("Form", form);
        }

        CopyInto(entry, form);
        await AttachPhotos(entry, photos);
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

    private void ApplyRules(EntryFormViewModel form)
    {
        // An unparseable datetime binds to default(DateTime), which cannot be
        // turned into an instant — catch it before the conversion throws.
        if (form.OccurredAt == default)
        {
            ModelState.AddModelError(nameof(EntryFormViewModel.OccurredAt), "Enter a valid date and time.");
        }

        foreach (var error in EntryRules.Validate(form.Type, form.Severity, form.PillName, form.Note))
        {
            ModelState.AddModelError(string.Empty, error);
        }
    }

    private static void CopyInto(Entry entry, EntryFormViewModel form)
    {
        var previousPillName = entry.PillName;

        entry.OccurredAt = AppTime.FromLocal(form.OccurredAt);
        entry.Note = Trimmed(form.Note);
        entry.Severity = EntryRules.RequiresSeverity(entry.Type) ? form.Severity : null;
        entry.PillName = EntryRules.RequiresPillName(entry.Type) ? Trimmed(form.PillName) : null;

        // DoseQuantity is deliberately absent: only a checklist tick ever sets it, and what it
        // recorded is the dose actually taken. An entry without one counts as a single unit.

        // MedStockId is the same kind of stamp, but naming the medication by hand contradicts it:
        // a tick recorded which stock this dose came out of, and typing a different name says it
        // came out of something else. Dropping the link puts the dose back on name matching,
        // which is what every hand-made dose follows. An unchanged name keeps the link, so
        // correcting a note or a time never disconnects a ticked dose from its stock.
        if (!MedStockRules.NamesMatch(entry.PillName, previousPillName))
        {
            entry.MedStockId = null;
        }
    }

    private void LogPhotosAttached(int entryId, List<IFormFile>? photos)
    {
        if (photos is { Count: > 0 })
        {
            _logger.LogInformation("Entry {EntryId} gained {PhotoCount} photo(s)", entryId, photos.Count);
        }
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private IActionResult RedirectToDay(DateOnly day) =>
        RedirectToAction(nameof(DayController.ByDate), "Day", new { date = AppTime.Key(day) });

    private void ValidatePhotos(List<IFormFile>? photos)
    {
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

    // Bytes are read straight into the Photo row; the app never touches disk.
    private static async Task AttachPhotos(Entry entry, List<IFormFile>? photos)
    {
        if (photos is null)
        {
            return;
        }

        foreach (var file in photos)
        {
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);

            entry.Photos.Add(new Photo
            {
                Data = stream.ToArray(),
                ContentType = file.ContentType,
                FileName = Path.GetFileName(file.FileName),
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
    }

    // Ids and names only — Data is never selected outside PhotosController.Get.
    private async Task<List<PhotoSummary>> LoadPhotoSummaries(int entryId) =>
        await _db.Photos
            .Where(p => p.EntryId == entryId)
            .OrderBy(p => p.CreatedAt)
            .Select(p => new PhotoSummary { Id = p.Id, FileName = p.FileName })
            .ToListAsync();
}
