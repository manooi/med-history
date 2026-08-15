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

    /// <summary>
    /// The <see cref="MedAllocation"/> this entry ticked a slot of, when it was created from the
    /// day's checklist rather than typed in. Null on every hand-made entry.
    ///
    /// Deliberately not a foreign key, the same stance <see cref="Type"/> takes: an allocation is
    /// a plan for one day and the user may delete it, but the dose it recorded actually happened
    /// and must survive. A dangling id is expected, not a fault — the checklist ignores ticks it
    /// finds no allocation for.
    /// </summary>
    public int? ChecklistAllocationId { get; set; }

    /// <summary>
    /// Which slot of that allocation was ticked, as the canonical slot name — see
    /// <see cref="Services.MedPlanRules.SlotName"/>. Set together with
    /// <see cref="ChecklistAllocationId"/>; the pair is what identifies a tick.
    /// </summary>
    public string? ChecklistSlot { get; set; }

    public List<Photo> Photos { get; set; } = [];
}
