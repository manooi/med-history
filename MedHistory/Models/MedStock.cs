namespace MedHistory.Models;

/// <summary>
/// How much of one medication is on hand. One row per medication name, and the name is the
/// whole of the link to the doses that draw it down: a dose is a Pill <see cref="Entry"/>
/// carrying a <see cref="Entry.PillName"/>, and nothing else ties the two together. That is
/// deliberate — a dose typed in by hand is as real as one ticked off the checklist, and both
/// have to count.
///
/// Only the total is stored. What has been consumed is summed from the entries every time a
/// page renders, never written here, so deleting a logged dose puts its units back with no
/// bookkeeping to go wrong. A refill is the user raising this total.
/// </summary>
public class MedStock
{
    /// <summary>
    /// Matches <see cref="MedAllocation.NameMaxLength"/> on purpose: the two name the same
    /// medication, so a name that fits a plan must fit a stock row.
    /// </summary>
    public const int NameMaxLength = MedAllocation.NameMaxLength;

    public int Id { get; set; }

    /// <summary>Stored trimmed, in the casing it was typed; unique case-insensitively.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Units stocked, not units left — what was bought, which the user raises on a refill.
    /// Never negative; what is left of it may well be.
    /// </summary>
    public decimal TotalCount { get; set; }
}
