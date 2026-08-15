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

    /// <summary>
    /// Units this dose was, copied from the allocation's
    /// <see cref="MedAllocation.DoseQuantity"/> when the slot was ticked. A historical fact, not
    /// a reference: a later edit to the plan changes what the next dose will be and leaves this
    /// alone, which is the whole reason the number is stored here rather than looked up.
    ///
    /// Null on every entry the user typed in by hand, and on every dose ticked before quantities
    /// existed. Both count as one unit wherever doses are totalled — see
    /// <see cref="Services.MedStockRules"/>.
    /// </summary>
    public decimal? DoseQuantity { get; set; }

    /// <summary>
    /// The <see cref="MedStock"/> row this dose came out of, stamped from the allocation when the
    /// slot was ticked — a historical fact alongside <see cref="PillName"/> and
    /// <see cref="DoseQuantity"/>, not a reference to be re-read.
    ///
    /// This is what makes renaming safe. The name is display and the id is identity, so renaming
    /// the stock row, or renaming the plan that feeds it, leaves every dose already counted
    /// against that stock still counted. Joined by name alone, as it was, a rename silently
    /// disconnected everything logged before it and the remaining count jumped.
    ///
    /// Null on every entry the user typed in by hand, and on every dose ticked before doses were
    /// linked by id. Those are matched to a stock by <see cref="PillName"/> instead, so a manual
    /// dose follows whatever the stock is called now. Deliberately not a foreign key, the same
    /// stance <see cref="Type"/> and <see cref="ChecklistAllocationId"/> take: a dangling id — the
    /// stock row having since been removed — is expected, not a fault, and counts toward nothing.
    /// </summary>
    public int? MedStockId { get; set; }

    public List<Photo> Photos { get; set; } = [];
}
