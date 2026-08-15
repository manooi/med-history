namespace MedHistory.Models;

/// <summary>
/// The six types the app ships with. They are rows in the EntryTypes table like every
/// other type; these constants exist only because those six carry type-specific fields
/// (severity, pill name, a required note) that <see cref="Services.EntryRules"/> attaches
/// by name. Types added at runtime are name-only — note, photos and a timestamp.
/// </summary>
public static class BuiltInEntryTypes
{
    public const string Symptom = "Symptom";
    public const string Bleeding = "Bleeding";
    public const string Med = "Med";
    public const string Cough = "Cough";
    public const string Meal = "Meal";

    /// <summary>Free-text entry: note, photos and a timestamp — same shape as a
    /// user-added type, except the note is required (an empty Note entry is meaningless).</summary>
    public const string Note = "Note";

    /// <summary>
    /// Seed order and display order. The first five are unchanged from the declaration
    /// order of the <c>EntryType</c> enum this replaced — the "+" buttons keep the layout
    /// the user knows. Note was added later and appended last so it doesn't reshuffle
    /// existing layouts.
    /// </summary>
    public static readonly IReadOnlyList<string> All = [Symptom, Bleeding, Med, Cough, Meal, Note];
}
