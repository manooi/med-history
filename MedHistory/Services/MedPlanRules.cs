using MedHistory.Models;

namespace MedHistory.Services;

/// <summary>
/// The vocabulary of a medication plan — how a slot set is written down, and how slots, meal
/// relations and methods read to a human. Pure: no clock, no database, no HTTP.
///
/// A slot set is stored as a comma-separated list of names in day order with no spaces
/// ("Morning,Evening"), not as the flags enum's own <c>ToString</c> ("Morning, Evening") and
/// not as an integer. The format is pinned here so a stored value stays stable and legible in
/// psql however .NET chooses to render a <c>[Flags]</c> enum, and it keeps the column in line
/// with the enums-as-strings convention the rest of the schema follows.
///
/// Nothing here throws on bad input. Slot names arrive from form posts and from the database,
/// and a name that is not one of the four is dropped rather than rejected: the worst case is
/// an allocation that renders with fewer slot controls, never a day view that will not load.
/// </summary>
public static class MedPlanRules
{
    /// <summary>Wide enough for all four names and their separators, with room to spare.</summary>
    public const int SlotsMaxLength = 64;

    /// <summary>Longest a single stored slot name is, i.e. what an entry's slot column holds.</summary>
    public const int SlotNameMaxLength = 16;

    /// <summary>Every slot, in the order they occur in a day — the display order everywhere.</summary>
    public static readonly IReadOnlyList<MedSlots> AllSlots =
        [MedSlots.Morning, MedSlots.Noon, MedSlots.Evening, MedSlots.Bedtime];

    private const char Separator = ',';

    private const string PartSeparator = " · ";

    /// <summary>Canonical stored name of one slot, e.g. <c>Morning</c>.</summary>
    public static string SlotName(MedSlots slot) => slot.ToString();

    /// <summary>The set as stored: names in day order, comma separated. Empty for no slots.</summary>
    public static string FormatSlots(MedSlots slots) =>
        string.Join(Separator, Each(slots).Select(SlotName));

    /// <summary>
    /// The slots a set contains, in day order. Bits outside the four named slots are ignored.
    /// </summary>
    public static IReadOnlyList<MedSlots> Each(MedSlots slots) =>
        AllSlots.Where(slot => slots.HasFlag(slot)).ToList();

    public static int SlotCount(MedSlots slots) => Each(slots).Count;

    /// <summary>Reads a stored slot set back. Unknown or malformed names contribute nothing.</summary>
    public static MedSlots ParseSlots(string? raw) =>
        raw is null ? MedSlots.None : ParseSlots(raw.Split(Separator));

    /// <summary>
    /// Builds a set from loose names — what the maintenance form's checkboxes post. Repeats are
    /// harmless: setting a bit twice is setting it once.
    /// </summary>
    public static MedSlots ParseSlots(IEnumerable<string?> names)
    {
        var slots = MedSlots.None;

        foreach (var name in names)
        {
            if (TryParseSlot(name, out var slot))
            {
                slots |= slot;
            }
        }

        return slots;
    }

    /// <summary>
    /// One slot by name — how an entry's stored <c>ChecklistSlot</c> and the slot segment of a
    /// tick URL are read. Case- and whitespace-insensitive, since both come off the wire.
    /// False (and <see cref="MedSlots.None"/>) when the name is not a single known slot.
    /// </summary>
    public static bool TryParseSlot(string? name, out MedSlots slot)
    {
        var trimmed = name?.Trim();

        slot = AllSlots.FirstOrDefault(
            candidate => string.Equals(SlotName(candidate), trimmed, StringComparison.OrdinalIgnoreCase));

        return slot != MedSlots.None;
    }

    /// <summary>Human label for one slot. Empty for anything that is not a single known slot.</summary>
    public static string SlotLabel(MedSlots slot) => slot switch
    {
        MedSlots.Morning => "morning",
        MedSlots.Noon => "noon",
        MedSlots.Evening => "evening",
        MedSlots.Bedtime => "bedtime",
        _ => string.Empty
    };

    /// <summary>
    /// Human label for a meal relation. Empty for <see cref="MealRelation.None"/> — "it does not
    /// matter" is worth nothing on screen, and an empty label is what the composers drop.
    /// </summary>
    public static string MealRelationLabel(MealRelation relation) => relation switch
    {
        MealRelation.BeforeMeal => "before meal",
        MealRelation.AfterMeal => "after meal",
        MealRelation.WithMeal => "with meal",
        _ => string.Empty
    };

    /// <summary>
    /// Human label for a method. Empty for <see cref="MedMethod.Other"/>, which names no
    /// particular way of taking something and so reads as noise next to the medication.
    /// </summary>
    public static string MethodLabel(MedMethod method) => method switch
    {
        MedMethod.Eat => "eat",
        MedMethod.Apply => "apply",
        MedMethod.Eyedrop => "eyedrop",
        MedMethod.Inject => "inject",
        _ => string.Empty
    };

    /// <summary>
    /// How a meal relation reads in the maintenance form's dropdown, where every member needs a
    /// name — including the one that says nothing on a checklist row.
    /// </summary>
    public static string MealRelationOption(MealRelation relation) =>
        relation == MealRelation.None ? "any time" : MealRelationLabel(relation);

    /// <summary>How a method reads in the maintenance form's dropdown. See <see cref="MealRelationOption"/>.</summary>
    public static string MethodOption(MedMethod method) =>
        method == MedMethod.Other ? "other" : MethodLabel(method);

    /// <summary>
    /// How an allocation reads under its name — "after meal · eyedrop". Empty when the
    /// allocation says nothing beyond its slots.
    /// </summary>
    public static string DescribeAllocation(MealRelation relation, MedMethod method) =>
        Join(MealRelationLabel(relation), MethodLabel(method));

    /// <summary>
    /// The note a ticked slot writes onto its entry — "morning · after meal · eyedrop". The
    /// timeline shows notes as typed, so this is what makes a ticked dose legible there without
    /// the reader having to hold the checklist in their head.
    /// </summary>
    public static string ComposeNote(MedSlots slot, MealRelation relation, MedMethod method) =>
        Join(SlotLabel(slot), MealRelationLabel(relation), MethodLabel(method));

    /// <summary>Joins the parts that have something to say, in the separator the timeline uses.</summary>
    private static string Join(params string[] parts) =>
        string.Join(PartSeparator, parts.Where(part => !string.IsNullOrEmpty(part)));
}
