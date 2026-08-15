using MedHistory.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Controllers;

public class PhotosController : Controller
{
    private readonly AppDbContext _db;

    public PhotosController(AppDbContext db)
    {
        _db = db;
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
            return NotFound();
        }

        return File(photo.Data, photo.ContentType);
    }

    [HttpPost("/photos/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
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

        return RedirectToAction(nameof(EntriesController.Edit), "Entries", new { id = entryId });
    }
}
