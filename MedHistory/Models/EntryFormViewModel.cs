namespace MedHistory.Models;

public class EntryFormViewModel
{
    /// <summary>Null for a new entry.</summary>
    public int? Id { get; set; }

    /// <summary>Chosen at creation and immutable afterwards.</summary>
    public EntryType Type { get; set; }

    /// <summary>Local wall-clock time as typed into the datetime-local input.</summary>
    public DateTime OccurredAt { get; set; }

    public string? Note { get; set; }

    public Severity? Severity { get; set; }

    public string? PillName { get; set; }

    /// <summary>Photos already attached to the entry; empty for a new entry.</summary>
    public IReadOnlyList<PhotoSummary> ExistingPhotos { get; set; } = [];

    public bool IsEdit => Id.HasValue;
}

/// <summary>Display-only projection of a Photo — never carries the image bytes.</summary>
public class PhotoSummary
{
    public required int Id { get; init; }

    public required string FileName { get; init; }
}
