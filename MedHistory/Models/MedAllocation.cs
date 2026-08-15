namespace MedHistory.Models;

/// <summary>
/// The times of day a medication is taken at. A flags enum because an allocation is a
/// <em>set</em> of slots — "morning and bedtime" is one row, not two — and because a set of
/// four fixed members is cheaper to hold, compare and store as bits than as a collection.
/// Persisted as a canonical comma-separated name list; see
/// <see cref="Services.MedPlanRules.FormatSlots"/>.
/// </summary>
[Flags]
public enum MedSlots
{
    None = 0,
    Morning = 1,
    Noon = 2,
    Evening = 4,
    Bedtime = 8
}

/// <summary>When the dose sits relative to eating. <c>None</c> means it does not matter.</summary>
public enum MealRelation
{
    None,
    BeforeMeal,
    AfterMeal,
    WithMeal
}

/// <summary>How the dose is administered. <c>Other</c> is the escape hatch.</summary>
public enum MedMethod
{
    Eat,
    Apply,
    Eyedrop,
    Inject,
    Other
}

/// <summary>
/// A medication the user has allocated to one day: what it is, when in the day it is taken,
/// how much, and how. The slots are the plan — one dose per slot, so the day's requirement is
/// simply how many slots are set, and there is no separate count to keep in step with them.
///
/// The row holds no foreign key to the entries a tick creates; the link runs the other way,
/// from <see cref="Entry.ChecklistAllocationId"/>, so deleting an allocation leaves the doses
/// already logged under it untouched.
/// </summary>
public class MedAllocation
{
    public const int NameMaxLength = 64;

    public int Id { get; set; }

    /// <summary>Local calendar day, stored as a Postgres <c>date</c> — never an instant.</summary>
    public DateOnly Day { get; set; }

    /// <summary>Stored trimmed, in the casing it was typed; matched case-insensitively.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Times of day this is taken at; at least one. One slot is one dose.</summary>
    public MedSlots Slots { get; set; }

    /// <summary>
    /// Units taken at each slot — two tablets, half a tablet, one drop. It applies to every
    /// slot alike: a plan that differs between morning and bedtime is two allocations, not one
    /// row carrying two quantities.
    ///
    /// This is the plan as it stands now. What a dose actually was is stamped onto the entry
    /// the moment it is ticked — see <see cref="Entry.DoseQuantity"/> — so editing this never
    /// rewrites a dose already taken. Bounds and step live in
    /// <see cref="Services.MedPlanRules"/>.
    /// </summary>
    public decimal DoseQuantity { get; set; } = Services.MedPlanRules.DefaultDoseQuantity;

    public MealRelation MealRelation { get; set; }

    public MedMethod Method { get; set; }
}
