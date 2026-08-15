using MedHistory.Models;

namespace MedHistory.Services;

/// <summary>What a vote POST does to the day's row: create or replace it, or remove it outright.
/// See <see cref="AnxietyRules.DecideVote"/>.</summary>
public enum VoteAction
{
    Set,
    Clear
}

/// <summary>One day of the anxiety report: the day, and the level voted for it, if any. A day
/// with no vote is not a missing cell — every real day of the month gets one, see
/// <see cref="AnxietyRules.BuildMonth"/> — it is a cell whose <see cref="Level"/> is null.</summary>
public readonly record struct AnxietyDay(DateOnly Day, AnxietyLevel? Level);

/// <summary>One row of the calendar: seven cells, Monday first, matching
/// <see cref="ReportWeek"/>'s shape for the med report.</summary>
public readonly record struct AnxietyWeek(IReadOnlyList<AnxietyDay?> Days);

/// <summary>
/// A month as the anxiety report page renders it: the grid, and how many of its days carry a
/// vote. There is no average here — the levels are categories to look at on a calendar, not a
/// quantity to summarise into one number.
/// </summary>
public sealed record AnxietyMonth(
    DateOnly FirstDay,
    IReadOnlyList<AnxietyWeek> Weeks,
    int VotedCount)
{
    public string Key => ReportRules.MonthKey(FirstDay);

    public string Label => ReportRules.MonthLabel(FirstDay);

    public string PreviousKey => ReportRules.MonthKey(FirstDay.AddMonths(-1));

    public string NextKey => ReportRules.MonthKey(FirstDay.AddMonths(1));

    public string ProgressLabel => $"{VotedCount} voted";
}

/// <summary>
/// Pure anxiety-vote rules — no clock, no database, no HTTP. One level per day, editable: voting
/// the same level again clears the day rather than leaving it stuck, which is what makes the day
/// widget's buttons double as an undo without a separate control.
/// </summary>
public static class AnxietyRules
{
    /// <summary>Every level, calmest first — the order the day widget's buttons and the report's
    /// legend both read in.</summary>
    public static readonly IReadOnlyList<AnxietyLevel> Levels =
        [AnxietyLevel.Calm, AnxietyLevel.Ok, AnxietyLevel.Tense, AnxietyLevel.Anxious, AnxietyLevel.Panic];

    /// <summary>Lowercase name, e.g. "tense" — what a vote button reads and what the report's
    /// legend spells each level out as.</summary>
    public static string Label(AnxietyLevel level) => level switch
    {
        AnxietyLevel.Calm => "calm",
        AnxietyLevel.Ok => "ok",
        AnxietyLevel.Tense => "tense",
        AnxietyLevel.Anxious => "anxious",
        AnxietyLevel.Panic => "panic",
        _ => string.Empty
    };

    /// <summary>1 through 5 — the number a voted report cell shows, and the shading it picks.
    /// Just the enum's own value, named here so nothing outside this file reasons about the enum
    /// numbering directly.</summary>
    public static int Value(AnxietyLevel level) => (int)level;

    /// <summary>One face per level, calmest first — what a vote button and a voted report cell
    /// both show instead of the level's number.</summary>
    public static string Emoji(AnxietyLevel level) => level switch
    {
        AnxietyLevel.Calm => "😌",
        AnxietyLevel.Ok => "🙂",
        AnxietyLevel.Tense => "😟",
        AnxietyLevel.Anxious => "😰",
        AnxietyLevel.Panic => "😱",
        _ => string.Empty
    };

    /// <summary>
    /// One level by name — how the tick/clear route's <c>level</c> segment is read. Case- and
    /// whitespace-insensitive, the same tolerance <see cref="MedPlanRules.TryParseSlot"/> gives a
    /// checklist slot name, since both come off the wire rather than out of code. False for
    /// anything that is not exactly one of the five level names.
    /// </summary>
    public static bool TryParseLevel(string? name, out AnxietyLevel level)
    {
        var trimmed = name?.Trim();

        foreach (var candidate in Levels)
        {
            if (string.Equals(candidate.ToString(), trimmed, StringComparison.OrdinalIgnoreCase))
            {
                level = candidate;
                return true;
            }
        }

        level = default;
        return false;
    }

    /// <summary>
    /// What a vote POST for <paramref name="requested"/> should do given the day's current vote,
    /// if any. Voting the level already set clears the day — a second tap is how the day widget
    /// undoes a vote, there being no separate clear control. Voting any other level (including
    /// when nothing is set yet) replaces it.
    /// </summary>
    public static VoteAction DecideVote(AnxietyLevel? existing, AnxietyLevel requested) =>
        existing == requested ? VoteAction.Clear : VoteAction.Set;

    /// <summary>
    /// The whole report for one month: the grid and how many of its days were voted. Votes
    /// outside the month contribute nothing, so a caller that over-fetches cannot bend the
    /// month's count — the same guarantee <see cref="ReportRules.BuildMonth"/> gives the med
    /// report. Grid shape comes straight from <see cref="ReportRules.BuildWeeks{TCell}"/>; only
    /// what each cell holds is this report's own.
    /// </summary>
    public static AnxietyMonth BuildMonth(DateOnly month, IEnumerable<AnxietyVote> votes)
    {
        var first = ReportRules.FirstOfMonth(month);

        var byDay = votes
            .Where(vote => ReportRules.FirstOfMonth(vote.Day) == first)
            .ToDictionary(vote => vote.Day, vote => vote.Level);

        var weeks = ReportRules
            .BuildWeeks<AnxietyDay>(
                first, day => new AnxietyDay(day, byDay.TryGetValue(day, out var level) ? level : null))
            .Select(week => new AnxietyWeek(week))
            .ToList();

        return new AnxietyMonth(first, weeks, byDay.Count);
    }
}
