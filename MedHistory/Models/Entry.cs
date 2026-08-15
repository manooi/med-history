namespace MedHistory.Models;

public enum EntryType
{
    Symptom,
    Bleeding,
    Pill,
    Cough,
    Meal
}

public enum Severity
{
    Light,
    Moderate,
    Severe
}

public class Entry
{
    public int Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public EntryType Type { get; set; }

    public string? Note { get; set; }

    // Only meaningful for Bleeding and Cough entries.
    public Severity? Severity { get; set; }

    // Only meaningful for Pill entries.
    public string? PillName { get; set; }

    public List<Photo> Photos { get; set; } = [];
}
