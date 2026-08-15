using MedHistory.Models;
using MedHistory.Services;

namespace MedHistory.Tests;

public class AnxietyRulesTests
{
    private static readonly DateOnly August = new(2026, 8, 1);

    private static AnxietyVote Vote(DateOnly day, AnxietyLevel level) => new() { Day = day, Level = level };

    private static IReadOnlyList<AnxietyDay?> Cells(IReadOnlyList<AnxietyWeek> weeks) =>
        weeks.SelectMany(week => week.Days).ToList();

    // ---- TryParseLevel ----

    [Theory]
    [InlineData("Calm", AnxietyLevel.Calm)]
    [InlineData("Ok", AnxietyLevel.Ok)]
    [InlineData("Tense", AnxietyLevel.Tense)]
    [InlineData("Anxious", AnxietyLevel.Anxious)]
    [InlineData("Panic", AnxietyLevel.Panic)]
    public void TryParseLevel_EveryLevelName_Parses(string name, AnxietyLevel expected)
    {
        Assert.True(AnxietyRules.TryParseLevel(name, out var level));
        Assert.Equal(expected, level);
    }

    [Theory]
    [InlineData("calm")]
    [InlineData("CALM")]
    [InlineData(" Calm ")]
    public void TryParseLevel_CasingAndPadding_StillParses(string name)
    {
        // Same tolerance MedPlanRules.TryParseSlot gives a checklist slot name — both come off
        // a route segment, not out of code.
        Assert.True(AnxietyRules.TryParseLevel(name, out var level));
        Assert.Equal(AnxietyLevel.Calm, level);
    }

    [Theory]
    [InlineData("Calmness")]
    [InlineData("Worried")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryParseLevel_Unrecognised_Fails(string? name)
    {
        Assert.False(AnxietyRules.TryParseLevel(name, out var level));
        Assert.Equal(default, level);
    }

    // ---- Label / Value ----

    [Theory]
    [InlineData(AnxietyLevel.Calm, "calm", 1)]
    [InlineData(AnxietyLevel.Ok, "ok", 2)]
    [InlineData(AnxietyLevel.Tense, "tense", 3)]
    [InlineData(AnxietyLevel.Anxious, "anxious", 4)]
    [InlineData(AnxietyLevel.Panic, "panic", 5)]
    public void LabelAndValue_EveryLevel_ReadAsExpected(AnxietyLevel level, string label, int value)
    {
        Assert.Equal(label, AnxietyRules.Label(level));
        Assert.Equal(value, AnxietyRules.Value(level));
    }

    [Fact]
    public void Levels_AreOrderedCalmestFirst()
    {
        Assert.Equal(
            [AnxietyLevel.Calm, AnxietyLevel.Ok, AnxietyLevel.Tense, AnxietyLevel.Anxious, AnxietyLevel.Panic],
            AnxietyRules.Levels);
    }

    // ---- DecideVote ----

    [Fact]
    public void DecideVote_NothingSetYet_Sets()
    {
        Assert.Equal(VoteAction.Set, AnxietyRules.DecideVote(null, AnxietyLevel.Tense));
    }

    [Fact]
    public void DecideVote_SameLevelAgain_Clears()
    {
        Assert.Equal(VoteAction.Clear, AnxietyRules.DecideVote(AnxietyLevel.Tense, AnxietyLevel.Tense));
    }

    [Theory]
    [InlineData(AnxietyLevel.Calm, AnxietyLevel.Panic)]
    [InlineData(AnxietyLevel.Panic, AnxietyLevel.Calm)]
    [InlineData(AnxietyLevel.Ok, AnxietyLevel.Tense)]
    public void DecideVote_DifferentLevel_Sets(AnxietyLevel existing, AnxietyLevel requested)
    {
        Assert.Equal(VoteAction.Set, AnxietyRules.DecideVote(existing, requested));
    }

    // ---- BuildMonth: grid shape ----

    [Fact]
    public void BuildMonth_EveryWeekIsSevenCells()
    {
        foreach (var month in new[] { new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 1), new DateOnly(2024, 2, 1) })
        {
            Assert.All(AnxietyRules.BuildMonth(month, []).Weeks, week => Assert.Equal(7, week.Days.Count));
        }
    }

    [Fact]
    public void BuildMonth_LeadingBlanks_MatchReportRules()
    {
        // 1 Aug 2026 is a Saturday: five blanks lead it — same rule the med report grid uses.
        var cells = Cells(AnxietyRules.BuildMonth(August, []).Weeks);

        Assert.Equal(5, cells.TakeWhile(cell => cell is null).Count());
    }

    [Fact]
    public void BuildMonth_FebruaryInALeapYear_HasTwentyNineDays()
    {
        var cells = Cells(AnxietyRules.BuildMonth(new DateOnly(2024, 2, 1), []).Weeks);

        Assert.Equal(29, cells.Count(cell => cell is not null));
        Assert.Equal(new DateOnly(2024, 2, 29), cells.Last(cell => cell is not null)!.Value.Day);
    }

    [Fact]
    public void BuildMonth_FebruaryInACommonYear_HasTwentyEightDays()
    {
        var cells = Cells(AnxietyRules.BuildMonth(new DateOnly(2026, 2, 1), []).Weeks);

        Assert.Equal(28, cells.Count(cell => cell is not null));
    }

    [Fact]
    public void BuildMonth_EveryRealDayGetsACell_VotedOrNot()
    {
        // Unlike the med report's NoPlan days, an anxiety day with no vote is still a cell —
        // there is no "nothing to do" state to distinguish it from.
        var cells = Cells(AnxietyRules.BuildMonth(August, []).Weeks);

        Assert.Equal(31, cells.Count(cell => cell is not null));
        Assert.All(cells.Where(cell => cell is not null), cell => Assert.Null(cell!.Value.Level));
    }

    [Fact]
    public void BuildMonth_AnyDayOfTheMonth_ReportsTheWholeMonth()
    {
        Assert.Equal(August, AnxietyRules.BuildMonth(new DateOnly(2026, 8, 23), []).FirstDay);
    }

    // ---- BuildMonth: votes ----

    [Fact]
    public void BuildMonth_VoteLandsOnItsOwnCell()
    {
        var cells = Cells(AnxietyRules.BuildMonth(August, [Vote(new DateOnly(2026, 8, 12), AnxietyLevel.Panic)]).Weeks);

        var cell = cells.Single(c => c?.Day == new DateOnly(2026, 8, 12))!.Value;
        Assert.Equal(AnxietyLevel.Panic, cell.Level);

        // Every other day in the month stays unvoted.
        Assert.All(
            cells.Where(c => c is not null && c.Value.Day != new DateOnly(2026, 8, 12)),
            c => Assert.Null(c!.Value.Level));
    }

    [Fact]
    public void BuildMonth_MultipleVotes_EachLandsOnItsOwnDay()
    {
        var month = AnxietyRules.BuildMonth(
            August,
            [
                Vote(new DateOnly(2026, 8, 1), AnxietyLevel.Calm),
                Vote(new DateOnly(2026, 8, 15), AnxietyLevel.Tense),
                Vote(new DateOnly(2026, 8, 31), AnxietyLevel.Anxious)
            ]);

        var cells = Cells(month.Weeks);

        Assert.Equal(AnxietyLevel.Calm, cells.Single(c => c?.Day == new DateOnly(2026, 8, 1))!.Value.Level);
        Assert.Equal(AnxietyLevel.Tense, cells.Single(c => c?.Day == new DateOnly(2026, 8, 15))!.Value.Level);
        Assert.Equal(AnxietyLevel.Anxious, cells.Single(c => c?.Day == new DateOnly(2026, 8, 31))!.Value.Level);
    }

    [Fact]
    public void BuildMonth_VoteOutsideTheMonth_Excluded()
    {
        // The caller may over-fetch; the month's grid and count must not bend when it does —
        // the same guarantee ReportRules.BuildMonth gives the med report.
        var month = AnxietyRules.BuildMonth(
            August,
            [
                Vote(new DateOnly(2026, 7, 31), AnxietyLevel.Panic),
                Vote(new DateOnly(2026, 9, 1), AnxietyLevel.Panic)
            ]);

        Assert.Equal(0, month.VotedCount);
        Assert.All(Cells(month.Weeks).Where(c => c is not null), c => Assert.Null(c!.Value.Level));
    }

    // ---- BuildMonth: voted count ----

    [Fact]
    public void BuildMonth_NoVotes_CountIsZero()
    {
        Assert.Equal(0, AnxietyRules.BuildMonth(August, []).VotedCount);
    }

    [Fact]
    public void BuildMonth_CountsOnlyVotedDaysInTheMonth()
    {
        var month = AnxietyRules.BuildMonth(
            August,
            [
                Vote(new DateOnly(2026, 8, 1), AnxietyLevel.Calm),
                Vote(new DateOnly(2026, 8, 2), AnxietyLevel.Ok),
                Vote(new DateOnly(2026, 9, 1), AnxietyLevel.Panic) // outside the month
            ]);

        Assert.Equal(2, month.VotedCount);
    }

    [Fact]
    public void ProgressLabel_ReadsAsCountVoted()
    {
        var month = AnxietyRules.BuildMonth(August, [Vote(new DateOnly(2026, 8, 1), AnxietyLevel.Calm)]);

        Assert.Equal("1 voted", month.ProgressLabel);
    }

    // ---- Month keys, shared with ReportRules ----

    [Fact]
    public void PreviousAndNextKeys_CrossTheYearBoundary()
    {
        var january = AnxietyRules.BuildMonth(new DateOnly(2026, 1, 1), []);
        var december = AnxietyRules.BuildMonth(new DateOnly(2026, 12, 1), []);

        Assert.Equal("2025-12", january.PreviousKey);
        Assert.Equal("2027-01", december.NextKey);
        Assert.Equal("2026-01", january.Key);
    }
}
