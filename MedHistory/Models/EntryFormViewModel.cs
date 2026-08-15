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

    public bool IsEdit => Id.HasValue;
}
