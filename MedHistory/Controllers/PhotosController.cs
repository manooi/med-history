using MedHistory.Data;
using MedHistory.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Controllers;

public class PhotosController : Controller
{
    private readonly AppDbContext _db;
    private readonly ILogger<PhotosController> _logger;

    public PhotosController(AppDbContext db, ILogger<PhotosController> logger)
    {
        _db = db;
        _logger = logger;
    }

    // The only place in the app allowed to select Photo.Data.
    [HttpGet("/photos/{id:int}")]
    [ResponseCache(Duration = 31536000, Location = ResponseCacheLocation.Client)]
    public async Task<IActionResult> Get(int id)
    {
        var photo = await _db.Photos
            .Where(p => p.Id == id)
            .Select(p => new { p.Data, p.ContentType })
            .FirstOrDefaultAsync();

        if (photo is null)
        {
            _logger.LogWarning("Photo {PhotoId} requested but not found", id);
            return NotFound();
        }

        return File(photo.Data, photo.ContentType);
    }

    // The only caller is the entry form, which comes back here afterwards — so the form's own
    // origin has to make the round trip too, or removing a photo mid-edit quietly forgets which
    // list the reader came from.
    [HttpPost("/photos/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, string? returnUrl)
    {
        // Projected: only the parent id is needed, never the image bytes.
        var entryId = await _db.Photos
            .Where(p => p.Id == id)
            .Select(p => (int?)p.EntryId)
            .FirstOrDefaultAsync();

        if (entryId is null)
        {
            return NotFound();
        }

        await _db.Photos.Where(p => p.Id == id).ExecuteDeleteAsync();

        _logger.LogInformation("Photo {PhotoId} deleted from entry {EntryId}", id, entryId);

        // Sanitized before it is handed back: a rejected origin drops out of the route values
        // entirely rather than riding on the edit page's address as dead weight.
        return RedirectToAction(nameof(EntriesController.Edit), "Entries", new
        {
            id = entryId,
            returnUrl = RedirectRules.Sanitize(returnUrl, Url.IsLocalUrl)
        });
    }
}
