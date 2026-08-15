using MedHistory.Models;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Data;

/// <summary>
/// The database side of an entry's photos: turning an upload into stored rows, and reading back
/// what is already attached — without ever selecting the bytes back out. See
/// <c>PhotosController.Get</c>, the only place in the app allowed to do that.
/// </summary>
public static class PhotoStore
{
    // Bytes are read straight into the Photo row; the app never touches disk.
    public static async Task AttachPhotosAsync(this Entry entry, List<IFormFile>? photos)
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
    public static async Task<List<PhotoSummary>> LoadPhotoSummariesAsync(this AppDbContext db, int entryId) =>
        await db.Photos
            .Where(p => p.EntryId == entryId)
            .OrderBy(p => p.CreatedAt)
            .Select(p => new PhotoSummary { Id = p.Id, FileName = p.FileName })
            .ToListAsync();
}
