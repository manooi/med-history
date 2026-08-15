namespace MedHistory.Models;

/// <summary>
/// How much of one medication is on hand. One row per medication, and a dose draws it down two
/// ways, because there are two kinds of dose:
/// <list type="bullet">
/// <item>a dose ticked off the checklist carries this row's id — see
/// <see cref="Entry.MedStockId"/> — and keeps counting here however the row is renamed;</item>
/// <item>a dose typed in by hand carries only a <see cref="Entry.PillName"/> and is matched to
/// this row by name, so it follows whatever the row is called now.</item>
/// </list>
/// A dose typed in by hand is as real as one ticked, and both have to count; the id exists so the
/// first kind stops depending on a name that is only ever display.
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

    /// <summary>
    /// Stored trimmed, in the casing it was typed; unique case-insensitively. Editable: a rename
    /// is safe by construction now that ticked doses hold this row's id rather than its name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Units stocked, not units left — what was bought, which the user raises on a refill.
    /// Never negative; what is left of it may well be.
    /// </summary>
    public decimal TotalCount { get; set; }
}
