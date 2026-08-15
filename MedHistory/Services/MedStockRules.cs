using MedHistory.Models;

namespace MedHistory.Services;

/// <summary>
/// Doses logged under one medication name — one contribution to a stock row's consumption.
/// The name is whatever the entries carried; matching it to a stock row is
/// <see cref="MedStockRules.DeriveRows"/>'s job, not the caller's.
/// </summary>
public readonly record struct MedUsage(string? Name, decimal Quantity);

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
/// A stock row is joined to the doses that draw it down by name alone, matched the way every
/// other medication name in the app is matched: trimmed and case-insensitive, via
/// <see cref="ChecklistRules.NamesMatch"/>. Consumption is derived on every render and never
/// stored, so it cannot drift from the entries it is counted from.
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
    /// Returns one message per broken rule; an empty list means the stock row may be added, and
    /// <paramref name="total"/> then holds the parsed total. The duplicate check is the friendly
    /// half of the unique index on lower(Name) that the database also enforces.
    /// </summary>
    public static IReadOnlyList<string> ValidateNewStock(
        string? rawName,
        string? rawTotal,
        IEnumerable<string> existingNames,
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

            if (existingNames.Any(existing => NamesMatch(existing, name)))
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
    /// against its name.
    ///
    /// The usage is re-summed here rather than trusted as already grouped: it arrives grouped by
    /// the database, which groups by the stored name, so two spellings of one medication reach
    /// this as two entries and have to be folded together by the app's own name matching. Usage
    /// naming nothing stocked contributes to no row and is simply dropped — an untracked
    /// medication is not an error.
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
                logged.Where(u => NamesMatch(u.Name, stock.Name)).Sum(u => u.Quantity)))
            .ToList();
    }

    /// <summary>
    /// What one entry contributes to a stock row. A dose logged before quantities existed, or
    /// typed in by hand where there is no field for one, counts as the single unit it was.
    /// </summary>
    public static decimal UsageQuantity(decimal? doseQuantity) =>
        doseQuantity ?? MedPlanRules.DefaultDoseQuantity;

    /// <summary>
    /// What is left of the stock a medication name draws on, or null when nothing stocks that
    /// name — which is what tells a checklist row to say nothing at all rather than "0 left".
    /// </summary>
    public static decimal? FindRemaining(IEnumerable<MedStockRow>? rows, string? name)
    {
        if (rows is null)
        {
            return null;
        }

        foreach (var row in rows)
        {
            if (NamesMatch(row.Name, name))
            {
                return row.Remaining;
            }
        }

        return null;
    }

    /// <summary>
    /// What a checklist row prints for its stock — "(18 left)", or empty when the medication is
    /// not stocked. A negative reads as "(-2 left)", which is the honest way to say the count
    /// has been overdrawn.
    /// </summary>
    public static string RemainingLabel(decimal? remaining) =>
        remaining is null ? string.Empty : $"({MedPlanRules.FormatQuantity(remaining.Value)} left)";
}
