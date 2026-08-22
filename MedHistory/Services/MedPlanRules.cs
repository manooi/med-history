using System.Globalization;
using MedHistory.Models;

namespace MedHistory.Services;

/// <summary>
/// The vocabulary of a medication plan — how a slot set and a dose quantity are written down,
/// and how slots, quantities, meal relations and methods read to a human. Pure: no clock, no
/// database, no HTTP.
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

    /// <summary>What a dose is worth unless the plan says otherwise: one unit per slot.</summary>
    public const decimal DefaultDoseQuantity = 1m;

    /// <summary>
    /// The granularity a dose may be planned in — quarter units, which is as finely as a
    /// tablet is realistically split. Quantities off the step are rejected rather than rounded:
    /// the column keeps two decimals, so accepting 0.3 would store something never typed.
    /// </summary>
    public const decimal DoseQuantityStep = 0.25m;

    /// <summary>Smallest plannable dose — one step. Zero would be a plan to take nothing.</summary>
    public const decimal MinDoseQuantity = DoseQuantityStep;

    /// <summary>A ceiling loose enough never to be met in practice, tight enough to catch a typo.</summary>
    public const decimal MaxDoseQuantity = 99m;

    private const char Separator = ',';

    /// <summary>
    /// What <see cref="DescribeAllocation"/> and <see cref="ComposeNote"/> join their parts with.
    ///
    /// Public because a description is joined here and taken back apart in the view: each part is
    /// a resource key of its own, so the day's checklist splits on this to look them up one at a
    /// time. That is one literal on purpose — a second copy in the view would drift the day this
    /// one changed, and the description would render as a single unlooked-up blob with every test
    /// still green.
    /// </summary>
    public const string PartSeparator = " · ";

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
    /// The clock time a retro tick (any day other than today) is stamped at for one slot —
    /// "sometime in the morning" and so on, standing in for a moment nobody actually recorded.
    /// Each is far enough from midnight to survive the local-day round trip at any real-world
    /// UTC offset, which is the same property noon alone used to rely on for every slot. Falls
    /// back to noon for anything that is not a single known slot, matching how
    /// <see cref="TryParseSlot"/> already treats bad input as "no slot" rather than an error.
    /// </summary>
    public static TimeOnly SlotTime(MedSlots slot) => slot switch
    {
        MedSlots.Morning => new TimeOnly(9, 0),
        MedSlots.Noon => new TimeOnly(12, 0),
        MedSlots.Evening => new TimeOnly(18, 0),
        MedSlots.Bedtime => new TimeOnly(22, 0),
        _ => new TimeOnly(12, 0)
    };

    /// <summary>
    /// Reads a quantity typed into a number input. Invariant culture on purpose: an
    /// <c>&lt;input type="number"&gt;</c> posts "1.5" whatever the browser's locale, while MVC's
    /// form binder reads it in the server's culture and would reject the dot wherever a comma is
    /// the decimal separator. False for anything that is not a plain decimal, blank included.
    /// </summary>
    public static bool TryParseQuantity(string? raw, out decimal quantity) =>
        decimal.TryParse(raw?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out quantity);

    /// <summary>
    /// A quantity as it reads on screen and as it goes back into a number input: no trailing
    /// zeros, so a <c>numeric(x,2)</c> column that comes back as 2.00 still shows as "2".
    /// Invariant so the decimal point matches what the form will post back.
    /// </summary>
    public static string FormatQuantity(decimal quantity) =>
        quantity.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// How a dose quantity reads beside a medication — "×2", or empty at one unit, which is the
    /// default and not worth the space. Deliberately unit-free: a plan may be eaten, applied or
    /// dropped into an eye, so "tablets" would be wrong as often as right.
    /// </summary>
    public static string QuantityLabel(decimal quantity) =>
        quantity == DefaultDoseQuantity ? string.Empty : $"×{FormatQuantity(quantity)}";

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
    /// The note a ticked slot writes onto its entry — "×2 · morning · after meal · eyedrop".
    /// The timeline shows notes as typed, so this is what makes a ticked dose legible there
    /// without the reader having to hold the checklist in their head. The quantity leads
    /// because it is the part a reader is most likely to be checking, and drops out entirely at
    /// one unit, leaving the note exactly as it read before quantities existed.
    /// </summary>
    public static string ComposeNote(
        MedSlots slot,
        MealRelation relation,
        MedMethod method,
        decimal quantity = DefaultDoseQuantity) =>
        Join(QuantityLabel(quantity), SlotLabel(slot), MealRelationLabel(relation), MethodLabel(method));

    /// <summary>Joins the parts that have something to say, in the separator the timeline uses.</summary>
    private static string Join(params string[] parts) =>
        string.Join(PartSeparator, parts.Where(part => !string.IsNullOrEmpty(part)));
}
