using MedHistory.Models;

namespace MedHistory.Services;

/// <summary>
/// Pure entry rules — no clock, no database, no HTTP. Controllers call these so
/// the same decisions can be unit tested without spinning up the app.
/// </summary>
public static class EntryRules
{
    public static bool RequiresSeverity(EntryType type) =>
        type is EntryType.Bleeding or EntryType.Cough;

    public static bool RequiresPillName(EntryType type) =>
        type is EntryType.Pill;

    public static bool RequiresNote(EntryType type) =>
        type is EntryType.Symptom or EntryType.Meal;

    /// <summary>
    /// Returns one message per broken rule; an empty list means the entry is valid.
    /// </summary>
    public static IReadOnlyList<string> Validate(EntryType type, Severity? severity, string? pillName, string? note)
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
                errors.Add("Pill name is required for Pill entries.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(pillName))
        {
            errors.Add($"Pill name does not apply to {type} entries.");
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
    public static string? DetailLine(EntryType type, Severity? severity, string? pillName, string? note)
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
