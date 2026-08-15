namespace MedHistory.Models;

/// <summary>
/// A medication the user has allocated to one day, with how many times it must be taken.
/// Ticking one off is a real Pill <see cref="Entry"/>, so progress is always derived by
/// counting entries — an allocation stores no done-count of its own and holds no foreign
/// key: the medication name is free text the user types, and deleting an allocation must
/// never disturb the entries already logged under that name.
/// </summary>
public class MedAllocation
{
    public const int NameMaxLength = 64;

    public int Id { get; set; }

    /// <summary>Local calendar day, stored as a Postgres <c>date</c> — never an instant.</summary>
    public DateOnly Day { get; set; }

    /// <summary>Stored trimmed, in the casing it was typed; matched case-insensitively.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Doses expected that day; at least 1.</summary>
    public int RequiredCount { get; set; } = 1;
}
