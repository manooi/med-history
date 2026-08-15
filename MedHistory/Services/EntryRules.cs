using MedHistory.Models;

namespace MedHistory.Services;

/// <summary>
/// Pure entry rules — no clock, no database, no HTTP. Controllers call these so
/// the same decisions can be unit tested without spinning up the app.
///
/// Types are identified by name (ordinal comparison — names come from the EntryTypes
/// table, and the app stores and compares them in their canonical casing). Only the
/// five built-ins carry type-specific fields; any other name is a user-added type,
/// which means no severity, no pill name, an optional note, and photos allowed.
/// </summary>
public static class EntryRules
{
    public static bool RequiresSeverity(string type) =>
        type is BuiltInEntryTypes.Bleeding or BuiltInEntryTypes.Cough;

    public static bool RequiresPillName(string type) =>
        type is BuiltInEntryTypes.Med;

    public static bool RequiresNote(string type) =>
        type is BuiltInEntryTypes.Symptom;

    /// <summary>
    /// Returns one message per broken rule; an empty list means the entry is valid.
    /// </summary>
    public static IReadOnlyList<string> Validate(string type, Severity? severity, string? pillName, string? note)
    {
        var errors = new List<string>();

        if (RequiresSeverity(type))
        {
            if (severity is null)
            {
                errors.Add($"Severity is required for {type} entries.");
            }
        }
        else if (severity is not null)
        {
            errors.Add($"Severity does not apply to {type} entries.");
        }

        if (RequiresPillName(type))
        {
            if (string.IsNullOrWhiteSpace(pillName))
            {
                errors.Add("Med name is required for Med entries.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(pillName))
        {
            errors.Add($"Med name does not apply to {type} entries.");
        }

        if (RequiresNote(type) && string.IsNullOrWhiteSpace(note))
        {
            errors.Add($"Note is required for {type} entries.");
        }

        return errors;
    }

    /// <summary>
    /// One-line summary shown under the type in the day timeline. Null when the
    /// entry carries nothing worth showing.
    /// </summary>
    public static string? DetailLine(string type, Severity? severity, string? pillName, string? note)
    {
        var parts = new List<string>(3);

        if (RequiresPillName(type) && !string.IsNullOrWhiteSpace(pillName))
        {
            parts.Add(pillName.Trim());
        }

        if (RequiresSeverity(type) && severity is not null)
        {
            parts.Add(severity.Value.ToString());
        }

        if (!string.IsNullOrWhiteSpace(note))
        {
            parts.Add(note.Trim());
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    /// <summary>
    /// Orders entries by <c>OccurredAt</c> ascending; entries with an equal timestamp
    /// are ordered by type name, alphabetically (ordinal string compare) — a stable
    /// tie-break that does not depend on the order types were added to the database.
    /// </summary>
    public static IReadOnlyList<T> OrderEntries<T>(
        IEnumerable<T> entries,
        Func<T, DateTimeOffset> occurredAtSelector,
        Func<T, string> typeSelector) =>
        entries
            .OrderBy(occurredAtSelector)
            .ThenBy(typeSelector, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Half-open [start, end) instant range covering one local calendar day.
    /// Both bounds are normalised to UTC: Npgsql rejects a DateTimeOffset with a
    /// non-zero offset when reading or writing <c>timestamp with time zone</c>.
    /// </summary>
    public static (DateTimeOffset Start, DateTimeOffset End) LocalDayRange(DateOnly day, TimeSpan offset)
    {
        var start = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), offset);
        return (start.ToUniversalTime(), start.AddDays(1).ToUniversalTime());
    }
}
