using MedHistory.Data;
using MedHistory.Models;
using MedHistory.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Controllers;

public class EntriesController : Controller
{
    private readonly AppDbContext _db;

    public EntriesController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("/entries/new")]
    public IActionResult New(EntryType type, string? date)
    {
        if (!Enum.IsDefined(type))
        {
            return BadRequest();
        }

        var day = AppTime.TryParseDay(date, out var parsed) ? parsed : AppTime.Today();

        return View("Form", new EntryFormViewModel
        {
            Type = type,
            OccurredAt = day.ToDateTime(TimeOnly.FromDateTime(DateTime.Now), DateTimeKind.Unspecified),
            Severity = EntryRules.RequiresSeverity(type) ? Models.Severity.Light : null
        });
    }

    [HttpPost("/entries")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(60 * 1024 * 1024)]
    public async Task<IActionResult> Create(EntryFormViewModel form, List<IFormFile>? photos)
    {
        if (!Enum.IsDefined(form.Type))
        {
            return BadRequest();
        }

        form.Id = null;
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

        // Type is fixed at creation; the posted value is never trusted.
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
        _db.Entries.Remove(entry);
        await _db.SaveChangesAsync();

        return RedirectToDay(day);
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
        entry.OccurredAt = AppTime.FromLocal(form.OccurredAt);
        entry.Note = Trimmed(form.Note);
        entry.Severity = EntryRules.RequiresSeverity(entry.Type) ? form.Severity : null;
        entry.PillName = EntryRules.RequiresPillName(entry.Type) ? Trimmed(form.PillName) : null;
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
