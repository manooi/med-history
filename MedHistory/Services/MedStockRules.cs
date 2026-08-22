using MedHistory.Models;

namespace MedHistory.Services;

/// <summary>
/// Doses logged against one stock link — one contribution to a stock row's consumption. Both
/// halves of the link are carried because a dose finds its stock either way: by
/// <paramref name="StockId"/> when a tick stamped one onto it, and by
/// <paramref name="Name"/> when nothing did. Which applies is
/// <see cref="MedStockRules.DeriveRows"/>'s job, not the caller's.
/// </summary>
public readonly record struct MedUsage(int? StockId, string? Name, decimal Quantity);

/// <summary>
/// An allocation reduced to what re-resolving its stock link needs: which row it is, the
/// medication it names, and the stock it points at now. See <see cref="MedStockRules.Relink"/>.
/// </summary>
public readonly record struct StockLink(int AllocationId, string? Name, int? StockId);

/// <summary>
/// One stock row as a page renders it: what was stocked, what has been taken against it, and
/// what that leaves.
/// </summary>
public readonly record struct MedStockRow(int Id, string Name, decimal Total, decimal Consumed)
{
    /// <summary>
    /// Goes negative once more has been logged than was ever stocked, and is shown that way.
    /// The count is the user's own bookkeeping — being behind on a refill is information, not
    /// an error to hide.
    /// </summary>
    public decimal Remaining => Total - Consumed;
}

/// <summary>
/// Pure medication-stock rules — no clock, no database, no HTTP. The meds page and the day
/// view make every stock decision through here so they can be unit tested without a database.
///
/// A stock row is joined to the doses that draw it down by id where there is one and by name
/// where there is not. A ticked dose carries the id of the stock it came out of, so it keeps
/// counting against that row however the row or the plan is renamed afterwards; a hand-typed
/// dose has only a name, matched the way every other medication name in the app is matched —
/// trimmed and case-insensitive, via <see cref="ChecklistRules.NamesMatch"/> — so it follows
/// whatever the row is called now. Consumption is derived on every render and never stored, so
/// it cannot drift from the entries it is counted from.
///
/// Nothing here decides whether a dose may be taken. Remaining is allowed to go negative
/// because a stock count that is behind reality must never be a reason the user cannot log
/// what they actually took.
/// </summary>
public static class MedStockRules
{
    public const int NameMaxLength = MedStock.NameMaxLength;

    /// <summary>
    /// Ceiling of the <c>numeric(7,2)</c> column the total is stored in. Postgres would reject
    /// anything past it outright, so the rules reject it first with something readable.
    /// </summary>
    public const decimal MaxTotal = 99_999.99m;

    /// <summary>Decimal places the total column keeps; more would be silently rounded away.</summary>
    public const int TotalDecimals = 2;

    /// <summary>
    /// Deliberately the same rule a medication name follows everywhere else — a stock row and
    /// an allocation name the same thing, so "Panadol " and "panadol" must not become two rows.
    /// </summary>
    public static string? NormalizeName(string? raw) => ChecklistRules.NormalizeName(raw);

    /// <summary>Whether two medication names mean the same medication. Same rule, same reason.</summary>
    public static bool NamesMatch(string? a, string? b) => ChecklistRules.NamesMatch(a, b);

    /// <summary>
    /// Returns one message per broken rule; an empty list means the stock row may be saved, and
    /// <paramref name="total"/> then holds the parsed total. One set of rules judges an add and an
    /// edit alike — a rename is the name half of an edit and a refill is the total half, and
    /// neither is worth its own set.
    ///
    /// <paramref name="otherNames"/> is every stocked name belonging to some row other than the
    /// one being judged: all of them on an add, all but the row's own on an edit, so a name left
    /// unchanged is never flagged as a duplicate of itself. The check is the friendly half of the
    /// unique index on lower(Name) that the database also enforces.
    /// </summary>
    public static IReadOnlyList<string> ValidateStock(
        string? rawName,
        string? rawTotal,
        IEnumerable<string> otherNames,
        out decimal total)
    {
        var errors = new List<string>();
        var name = NormalizeName(rawName);

        if (name is null)
        {
            errors.Add("Medication name is required.");
        }
        else
        {
            if (name.Length > NameMaxLength)
            {
                errors.Add($"Medication name must be {NameMaxLength} characters or fewer.");
            }

            if (otherNames.Any(existing => NamesMatch(existing, name)))
            {
                errors.Add($"\"{name}\" is already stocked.");
            }
        }

        errors.AddRange(ValidateTotal(rawTotal, out total));

        return errors;
    }

    /// <summary>
    /// Returns one message per broken rule for a stock total, and the parsed value when there
    /// are none. Refilling is editing this number upward, so the same rules judge an add and an
    /// edit. Zero is allowed — "I have run out" is a thing the user may want to record.
    /// </summary>
    public static IReadOnlyList<string> ValidateTotal(string? rawTotal, out decimal total)
    {
        total = 0m;

        if (string.IsNullOrWhiteSpace(rawTotal))
        {
            return ["Total is required."];
        }

        if (!MedPlanRules.TryParseQuantity(rawTotal, out var parsed))
        {
            return ["Total must be a number."];
        }

        if (parsed < 0m || parsed > MaxTotal)
        {
            return [$"Total must be between 0 and {MedPlanRules.FormatQuantity(MaxTotal)}."];
        }

        // Rejected rather than rounded: the column keeps two decimals, so storing 1.005 would
        // quietly turn it into something the user never typed.
        if (decimal.Round(parsed, TotalDecimals) != parsed)
        {
            return [$"Total must have at most {TotalDecimals} decimal places."];
        }

        total = parsed;

        return [];
    }

    /// <summary>
    /// Builds one row per stock, in the order given, each carrying what has been consumed
    /// against it.
    ///
    /// Doses reach a stock row by one of two routes, and which route applies is decided by
    /// whether the dose carries a stock id at all:
    /// <list type="bullet">
    /// <item><b>By id.</b> A dose ticked off the checklist was stamped with the id of the stock it
    /// came out of, and counts against that row whatever it or the plan that logged it is called
    /// now. This is what survives a rename.</item>
    /// <item><b>By name.</b> A dose with no id — typed in by hand, or ticked before doses were
    /// linked — has only the medication name it was written with, so it counts against whichever
    /// row carries that name today and moves with it if the row is renamed.</item>
    /// </list>
    /// The routes are split on the presence of the id, never tried in turn, so a dose is counted
    /// exactly once: an id-carrying dose whose name also happens to match some other row is
    /// counted against its id and nowhere else.
    ///
    /// Usage that names nothing stocked, or carries the id of a row since removed, contributes to
    /// no row and is dropped — an untracked medication is not an error, and neither is a dangling
    /// id.
    ///
    /// The name route is re-summed here rather than trusted as already grouped: it arrives grouped
    /// by the database, which groups by the stored name, so two spellings of one medication reach
    /// this as two entries and have to be folded together by the app's own name matching.
    /// </summary>
    public static IReadOnlyList<MedStockRow> DeriveRows(
        IEnumerable<MedStock> stocks,
        IEnumerable<MedUsage> usage)
    {
        var logged = usage.ToList();

        return stocks
            .Select(stock => new MedStockRow(
                stock.Id,
                stock.Name,
                stock.TotalCount,
                logged.Where(u => DrawsOn(u, stock)).Sum(u => u.Quantity)))
            .ToList();
    }

    /// <summary>
    /// Whether one group of logged doses draws down this stock row — the id where the doses carry
    /// one, the name where they do not. See <see cref="DeriveRows"/> for why it is one or the
    /// other and never both.
    /// </summary>
    private static bool DrawsOn(MedUsage usage, MedStock stock) =>
        usage.StockId is { } linked ? linked == stock.Id : NamesMatch(usage.Name, stock.Name);

    /// <summary>
    /// The id of the stock row a medication name draws on, or null when nothing stocks that name.
    ///
    /// This is the one place a name becomes a stock link. An allocation resolves through here when
    /// it is created or edited, and every allocation re-resolves through here whenever the stocked
    /// names change; after that the link is an id and the name is only what the user reads.
    /// </summary>
    public static int? ResolveStockId(IEnumerable<MedStock> stocks, string? name)
    {
        foreach (var stock in stocks)
        {
            if (NamesMatch(stock.Name, name))
            {
                return stock.Id;
            }
        }

        return null;
    }

    /// <summary>
    /// The allocations whose stock link no longer agrees with the stocked names, each carrying the
    /// link it should have — so a caller writes only the rows that actually change and leaves the
    /// rest alone.
    ///
    /// Every allocation is re-resolved rather than only those naming whichever stock row changed:
    /// one rename moves a name off one row and onto another in a single edit, an add can claim a
    /// name plans were already using, and a removal orphans links that now name nothing. Sweeping
    /// the lot is one pass over one person's medication plan — cheaper than being clever about it,
    /// and it cannot miss a case.
    /// </summary>
    public static IReadOnlyList<StockLink> Relink(
        IEnumerable<StockLink> allocations,
        IEnumerable<MedStock> stocks)
    {
        var stocked = stocks.ToList();
        var changed = new List<StockLink>();

        foreach (var allocation in allocations)
        {
            var resolved = ResolveStockId(stocked, allocation.Name);

            if (resolved != allocation.StockId)
            {
                changed.Add(allocation with { StockId = resolved });
            }
        }

        return changed;
    }

    /// <summary>
    /// What one entry contributes to a stock row. A dose logged before quantities existed, or
    /// typed in by hand where there is no field for one, counts as the single unit it was.
    /// </summary>
    public static decimal UsageQuantity(decimal? doseQuantity) =>
        doseQuantity ?? MedPlanRules.DefaultDoseQuantity;

    /// <summary>
    /// What is left of the stock a checklist row draws on, or null when nothing stocks it — which
    /// is what tells the row to say nothing at all rather than "0 left".
    ///
    /// The link is tried first and the name only as a fallback, which is exactly how the doses
    /// beneath it are counted: a row linked to a stock keeps reading that stock's count after
    /// either has been renamed, and an unlinked one shows whatever is stocked under its name. A
    /// link to a row since removed finds nothing and reads as unstocked, which it now is.
    /// </summary>
    public static decimal? FindRemaining(IEnumerable<MedStockRow>? rows, int? stockId, string? name)
    {
        if (rows is null)
        {
            return null;
        }

        var stocked = rows as IReadOnlyCollection<MedStockRow> ?? rows.ToList();

        if (stockId is { } linked)
        {
            foreach (var row in stocked)
            {
                if (row.Id == linked)
                {
                    return row.Remaining;
                }
            }

            return null;
        }

        foreach (var row in stocked)
        {
            if (NamesMatch(row.Name, name))
            {
                return row.Remaining;
            }
        }

        return null;
    }

    /// <summary>
    /// The resource key a checklist row prints for its stock, the count in <c>{0}</c>. The
    /// brackets belong to the template rather than to the caller, for the reason
    /// <see cref="ChecklistRules.MoreDaysKey"/> owns its comma: only a translation that owns the
    /// whole string can punctuate it its own way.
    /// </summary>
    public const string RemainingKey = "({0} left)";

    /// <summary>
    /// What a checklist row prints for its stock — <see cref="RemainingKey"/> and the count, or
    /// null when the medication is not stocked, which is not the same as none left and must not
    /// print as "(0 left)". A negative count is passed through and reads as "(-2 left)", the
    /// honest way to say the stock has been overdrawn.
    ///
    /// Key plus value rather than a sentence, the same shape
    /// <see cref="ChecklistRules.Progress"/> uses: the rules go on speaking English and the view
    /// looks the key up, so the number lands wherever the Thai puts it.
    /// </summary>
    // The null is cast on purpose: RuleMessage converts implicitly from string, so a bare null
    // binds to that conversion and yields a message with a null key instead of no message at all.
    public static RuleMessage? RemainingLabel(decimal? remaining) =>
        remaining is { } left
            ? new RuleMessage(RemainingKey, MedPlanRules.FormatQuantity(left))
            : (RuleMessage?)null;
}
