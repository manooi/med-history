using MedHistory.Models;

namespace MedHistory.Services;

/// <summary>Whether a type name may be chosen for a new entry.</summary>
public enum TypeAvailability
{
    Ok,
    Unknown,
    Inactive
}

/// <summary>
/// Pure entry-type rules — no clock, no database, no HTTP. The /types page and the
/// create-entry guard make every decision through here so they can be unit tested
/// without a database.
///
/// Names are compared case-insensitively throughout: the user should not be able to
/// add "cough" alongside the built-in "Cough".
/// </summary>
public static class EntryTypeRules
{
    public const int NameMaxLength = EntryTypeDef.NameMaxLength;

    /// <summary>
    /// Trims surrounding whitespace; returns null when nothing is left. Stored names
    /// are always this normalised form.
    /// </summary>
    public static string? NormalizeName(string? raw)
    {
        var trimmed = raw?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    public static bool NamesMatch(string? a, string? b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns one message per broken rule; an empty list means the name may be added.
    /// The duplicate check is the friendly half of the unique index on lower(Name) that
    /// the database also enforces.
    /// </summary>
    public static IReadOnlyList<string> ValidateNewName(string? raw, IEnumerable<string> existingNames)
    {
        var name = NormalizeName(raw);

        if (name is null)
        {
            return ["Type name is required."];
        }

        var errors = new List<string>();

        if (name.Length > NameMaxLength)
        {
            errors.Add($"Type name must be {NameMaxLength} characters or fewer.");
        }

        if (existingNames.Any(existing => NamesMatch(existing, name)))
        {
            errors.Add($"A type named \"{name}\" already exists.");
        }

        return errors;
    }

    /// <summary>
    /// Whether an entry may be created with this type name. Editing an existing entry
    /// does not go through here — its type comes from the stored row, so an entry whose
    /// type was since deactivated stays editable.
    /// </summary>
    public static TypeAvailability CheckAvailable(string? name, IEnumerable<(string Name, bool IsActive)> types)
    {
        var normalized = NormalizeName(name);

        if (normalized is null)
        {
            return TypeAvailability.Unknown;
        }

        foreach (var type in types)
        {
            if (NamesMatch(type.Name, normalized))
            {
                return type.IsActive ? TypeAvailability.Ok : TypeAvailability.Inactive;
            }
        }

        return TypeAvailability.Unknown;
    }

    /// <summary>
    /// Display order for every type list in the app: the six built-ins first in
    /// <see cref="BuiltInEntryTypes.All"/> order — so the day view's "+" buttons keep the
    /// layout they had when the order came from the enum — then user-added types
    /// alphabetically. Rank comes from the name rather than an IsBuiltIn flag so the same
    /// ordering applies to plain name lists, such as the type counts on the history page.
    /// </summary>
    public static IReadOnlyList<T> SortForDisplay<T>(IEnumerable<T> types, Func<T, string> nameSelector) =>
        types
            .OrderBy(t => BuiltInRank(nameSelector(t)))
            .ThenBy(nameSelector, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static int BuiltInRank(string name)
    {
        for (var i = 0; i < BuiltInEntryTypes.All.Count; i++)
        {
            if (NamesMatch(BuiltInEntryTypes.All[i], name))
            {
                return i;
            }
        }

        // Everything the app did not ship with sorts after every built-in.
        return BuiltInEntryTypes.All.Count;
    }
}
