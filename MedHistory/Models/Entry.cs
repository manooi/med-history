namespace MedHistory.Models;

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

    /// <summary>
    /// Name of an <see cref="EntryTypeDef"/>, held as plain text with no foreign key:
    /// deactivating a type must never touch the entries already logged under it, and a
    /// historical entry stays readable even if its type row is later gone. The app
    /// validates the name against the active types when an entry is created.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    public string? Note { get; set; }

    // Only meaningful for Bleeding and Cough entries.
    public Severity? Severity { get; set; }

    // Only meaningful for Pill entries.
    public string? PillName { get; set; }

    public List<Photo> Photos { get; set; } = [];
}
