namespace MedHistory.Models;

/// <summary>
/// A selectable entry type. These rows are the source of truth for what the day view
/// offers, so a new type is added by inserting a row rather than by editing code.
/// Names are unique case-insensitively; a type is retired by clearing
/// <see cref="IsActive"/>, never by deleting the row.
/// </summary>
public class EntryTypeDef
{
    public const int NameMaxLength = 32;

    public int Id { get; set; }

    /// <summary>Stored trimmed, in the casing it was typed; matched case-insensitively.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Inactive types disappear from the "+" buttons; their existing entries stay.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>True for the five seeded types, the only ones with type-specific fields.</summary>
    public bool IsBuiltIn { get; set; }
}
