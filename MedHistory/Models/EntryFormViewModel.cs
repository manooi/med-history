namespace MedHistory.Models;

public class EntryFormViewModel
{
    /// <summary>Null for a new entry.</summary>
    public int? Id { get; set; }

    /// <summary>Chosen at creation and immutable afterwards.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Local wall-clock time as typed into the datetime-local input.</summary>
    public DateTime OccurredAt { get; set; }

    public string? Note { get; set; }

    public Severity? Severity { get; set; }

    public string? PillName { get; set; }

    /// <summary>Photos already attached to the entry; empty for a new entry.</summary>
    public IReadOnlyList<PhotoSummary> ExistingPhotos { get; set; } = [];

    /// <summary>
    /// The page the form was opened from, so save, delete and cancel can hand the reader back
    /// to it. Null when there is none. Posted input — never used as a redirect target without
    /// <see cref="Services.RedirectRules"/> checking it first.
    /// </summary>
    public string? ReturnUrl { get; set; }

    public bool IsEdit => Id.HasValue;
}

/// <summary>Display-only projection of a Photo — never carries the image bytes.</summary>
public class PhotoSummary
{
    public required int Id { get; init; }

    public required string FileName { get; init; }
}
