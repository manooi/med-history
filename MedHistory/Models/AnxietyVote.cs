namespace MedHistory.Models;

/// <summary>
/// How the day felt, from calmest to most anxious. Ordered so the numeric value is itself the
/// severity — see <see cref="Services.AnxietyRules.Value"/> — and stored by name in Postgres,
/// like every enum in this schema.
/// </summary>
public enum AnxietyLevel
{
    Calm = 1,
    Ok = 2,
    Tense = 3,
    Anxious = 4,
    Panic = 5
}

/// <summary>
/// One day's anxiety vote — at most one per day, enforced by a unique index on <see cref="Day"/>.
/// Editable rather than a log: voting again for a day already voted either changes the level or
/// clears the vote outright, it never adds a second row — see
/// <see cref="Services.AnxietyRules.DecideVote"/>.
/// </summary>
public class AnxietyVote
{
    public int Id { get; set; }

    /// <summary>Local calendar day, stored as a Postgres <c>date</c> — never an instant.</summary>
    public DateOnly Day { get; set; }

    public AnxietyLevel Level { get; set; }
}
