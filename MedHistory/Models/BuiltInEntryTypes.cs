namespace MedHistory.Models;

/// <summary>
/// The five types the app ships with. They are rows in the EntryTypes table like every
/// other type; these constants exist only because those five carry type-specific fields
/// (severity, pill name) that <see cref="Services.EntryRules"/> attaches by name. Types
/// added at runtime are name-only — note, photos and a timestamp.
/// </summary>
public static class BuiltInEntryTypes
{
    public const string Symptom = "Symptom";
    public const string Bleeding = "Bleeding";
    public const string Pill = "Pill";
    public const string Cough = "Cough";
    public const string Meal = "Meal";

    /// <summary>
    /// Seed order and display order, unchanged from the declaration order of the
    /// <c>EntryType</c> enum this replaced — the "+" buttons keep the layout the user knows.
    /// </summary>
    public static readonly IReadOnlyList<string> All = [Symptom, Bleeding, Pill, Cough, Meal];
}
