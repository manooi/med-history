namespace MedHistory.Models;

public class Photo
{
    public int Id { get; set; }

    public int EntryId { get; set; }

    public Entry Entry { get; set; } = null!;

    public byte[] Data { get; set; } = [];

    public string ContentType { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
