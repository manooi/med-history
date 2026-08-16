using MedHistory.Services;

namespace MedHistory.Tests;

public class MeasurementRulesTests
{
    private static readonly DateOnly August = new(2026, 8, 1);

    private static MeasurementReading Reading(DateOnly day, decimal value, DateTimeOffset? occurredAt = null) =>
        new(day, value, occurredAt ?? day.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc));

    private static IReadOnlyList<MeasurementDay?> Cells(IEnumerable<MeasurementWeek> weeks) =>
        weeks.SelectMany(week => week.Days).ToList();

    // ---- TryParseValue: valid ----

    [Theory]
    [InlineData("72", 72)]
    [InlineData("72.5", 72.5)]
    [InlineData("72.50", 72.50)]
    [InlineData("0.25", 0.25)]
    [InlineData("999.99", 999.99)]
    [InlineData(" 72.5 ", 72.5)]
    public void TryParseValue_ValidValues_Parse(string raw, decimal expected)
    {
        Assert.True(MeasurementRules.TryParseValue(raw, out var value));
        Assert.Equal(expected, value);
    }

    // ---- TryParseValue: rejected ----

    [Fact]
    public void TryParseValue_Empty_Fails()
    {
        Assert.False(MeasurementRules.TryParseValue("", out _));
    }

    [Fact]
    public void TryParseValue_Null_Fails()
    {
        Assert.False(MeasurementRules.TryParseValue(null, out _));
    }

    [Fact]
    public void TryParseValue_Whitespace_Fails()
    {
        Assert.False(MeasurementRules.TryParseValue("   ", out _));
    }

    [Fact]
    public void TryParseValue_Zero_Fails()
    {
        Assert.False(MeasurementRules.TryParseValue("0", out _));
    }

    [Fact]
    public void TryParseValue_Negative_Fails()
    {
        Assert.False(MeasurementRules.TryParseValue("-5", out _));
    }

    [Fact]
    public void TryParseValue_AtOrAboveOneThousand_Fails()
    {
        Assert.False(MeasurementRules.TryParseValue("1000", out _));
        Assert.False(MeasurementRules.TryParseValue("1000.01", out _));
    }

    [Fact]
    public void TryParseValue_JustUnderOneThousand_Passes()
    {
        Assert.True(MeasurementRules.TryParseValue("999.99", out var value));
        Assert.Equal(999.99m, value);
    }

    [Fact]
    public void TryParseValue_ThreeDecimalPlaces_Fails()
    {
        Assert.False(MeasurementRules.TryParseValue("72.123", out _));
    }

    [Fact]
    public void TryParseValue_CommaDecimal_Fails()
    {
        // A comma-decimal locale's "70,5" would mean seventy point five, but with no thousands
        // separator allowed it must fail outright rather than silently read as seven hundred five.
        Assert.False(MeasurementRules.TryParseValue("70,5", out _));
    }

    [Fact]
    public void TryParseValue_Garbage_Fails()
    {
        Assert.False(MeasurementRules.TryParseValue("abc", out _));
    }

    [Fact]
    public void TryParseValue_OnFailure_ValueIsDefault()
    {
        MeasurementRules.TryParseValue("not a number", out var value);
        Assert.Equal(0m, value);
    }

    // ---- FormatValue ----

    [Theory]
    [InlineData(72, "72")]
    [InlineData(72.5, "72.5")]
    [InlineData(0.25, "0.25")]
    public void FormatValue_DropsTrailingZeros(decimal value, string expected)
    {
        Assert.Equal(expected, MeasurementRules.FormatValue(value));
    }

    // ---- BuildMonth: grid shape ----

    [Fact]
    public void BuildMonth_EveryWeekIsSevenCells()
    {
        foreach (var month in new[] { new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 1), new DateOnly(2024, 2, 1) })
        {
            Assert.All(MeasurementRules.BuildMonth(month, []).Weeks, week => Assert.Equal(7, week.Days.Count));
        }
    }

    [Fact]
    public void BuildMonth_LeadingBlanks_MatchReportRules()
    {
        // 1 Aug 2026 is a Saturday: five blanks lead it — same rule every other calendar report
        // in the app uses.
        var cells = Cells(MeasurementRules.BuildMonth(August, []).Weeks);

        Assert.Equal(5, cells.TakeWhile(cell => cell is null).Count());
    }

    [Fact]
    public void BuildMonth_EveryRealDayGetsACell_MeasuredOrNot()
    {
        var cells = Cells(MeasurementRules.BuildMonth(August, []).Weeks);

        Assert.Equal(31, cells.Count(cell => cell is not null));
        Assert.All(cells.Where(cell => cell is not null), cell => Assert.Null(cell!.Value.Value));
    }

    [Fact]
    public void BuildMonth_AnyDayOfTheMonth_ReportsTheWholeMonth()
    {
        Assert.Equal(August, MeasurementRules.BuildMonth(new DateOnly(2026, 8, 23), []).FirstDay);
    }

    // ---- BuildMonth: readings land on their own cell ----

    [Fact]
    public void BuildMonth_ReadingLandsOnItsOwnCell()
    {
        var cells = Cells(MeasurementRules.BuildMonth(
            August, [Reading(new DateOnly(2026, 8, 12), 72.5m)]).Weeks);

        var cell = cells.Single(c => c?.Day == new DateOnly(2026, 8, 12))!.Value;
        Assert.Equal(72.5m, cell.Value);

        Assert.All(
            cells.Where(c => c is not null && c.Value.Day != new DateOnly(2026, 8, 12)),
            c => Assert.Null(c!.Value.Value));
    }

    [Fact]
    public void BuildMonth_ReadingOutsideTheMonth_Excluded()
    {
        var month = MeasurementRules.BuildMonth(
            August,
            [
                Reading(new DateOnly(2026, 7, 31), 70m),
                Reading(new DateOnly(2026, 9, 1), 70m)
            ]);

        Assert.Equal(0, month.MeasuredCount);
        Assert.All(Cells(month.Weeks).Where(c => c is not null), c => Assert.Null(c!.Value.Value));
    }

    // ---- BuildMonth: latest-per-day selection ----

    [Fact]
    public void BuildMonth_TwoReadingsSameDay_LaterOneWins()
    {
        var day = new DateOnly(2026, 8, 12);
        var earlier = Reading(day, 72.0m, day.ToDateTime(new TimeOnly(7, 0), DateTimeKind.Utc));
        var later = Reading(day, 71.5m, day.ToDateTime(new TimeOnly(20, 0), DateTimeKind.Utc));

        var month = MeasurementRules.BuildMonth(August, [earlier, later]);
        var cell = Cells(month.Weeks).Single(c => c?.Day == day)!.Value;

        Assert.Equal(71.5m, cell.Value);
        Assert.Equal(1, month.MeasuredCount);
    }

    [Fact]
    public void BuildMonth_TwoReadingsSameDay_OrderInInput_DoesNotMatter()
    {
        var day = new DateOnly(2026, 8, 12);
        var earlier = Reading(day, 72.0m, day.ToDateTime(new TimeOnly(7, 0), DateTimeKind.Utc));
        var later = Reading(day, 71.5m, day.ToDateTime(new TimeOnly(20, 0), DateTimeKind.Utc));

        // Later reading listed first this time — the result must not depend on input order.
        var month = MeasurementRules.BuildMonth(August, [later, earlier]);
        var cell = Cells(month.Weeks).Single(c => c?.Day == day)!.Value;

        Assert.Equal(71.5m, cell.Value);
    }

    // ---- BuildMonth: stats ----

    [Fact]
    public void BuildMonth_NoReadings_StatsAreNull()
    {
        var month = MeasurementRules.BuildMonth(August, []);

        Assert.Null(month.Min);
        Assert.Null(month.Max);
        Assert.Null(month.Average);
        Assert.Equal(0, month.MeasuredCount);
    }

    [Fact]
    public void BuildMonth_MinMaxAverage_ComputedOverDistinctDays()
    {
        var month = MeasurementRules.BuildMonth(
            August,
            [
                Reading(new DateOnly(2026, 8, 1), 70m),
                Reading(new DateOnly(2026, 8, 2), 72m),
                Reading(new DateOnly(2026, 8, 3), 74m)
            ]);

        Assert.Equal(70m, month.Min);
        Assert.Equal(74m, month.Max);
        Assert.Equal(72m, month.Average);
        Assert.Equal(3, month.MeasuredCount);
    }

    [Fact]
    public void BuildMonth_Average_RoundsToOneDecimalPlace()
    {
        var month = MeasurementRules.BuildMonth(
            August,
            [
                Reading(new DateOnly(2026, 8, 1), 70m),
                Reading(new DateOnly(2026, 8, 2), 71m),
                Reading(new DateOnly(2026, 8, 3), 71m)
            ]);

        // (70 + 71 + 71) / 3 = 70.6666... -> 70.7
        Assert.Equal(70.7m, month.Average);
    }

    [Fact]
    public void BuildMonth_MinMaxAverage_IgnoreCollapsedSameDayReadings()
    {
        var day = new DateOnly(2026, 8, 1);

        // Two readings on one day must count once toward the stats, not twice.
        var month = MeasurementRules.BuildMonth(
            August,
            [
                Reading(day, 100m, day.ToDateTime(new TimeOnly(7, 0), DateTimeKind.Utc)),
                Reading(day, 70m, day.ToDateTime(new TimeOnly(20, 0), DateTimeKind.Utc))
            ]);

        Assert.Equal(70m, month.Min);
        Assert.Equal(70m, month.Max);
        Assert.Equal(70m, month.Average);
        Assert.Equal(1, month.MeasuredCount);
    }

    // ---- Month keys, shared with ReportRules ----

    [Fact]
    public void PreviousAndNextKeys_CrossTheYearBoundary()
    {
        var january = MeasurementRules.BuildMonth(new DateOnly(2026, 1, 1), []);
        var december = MeasurementRules.BuildMonth(new DateOnly(2026, 12, 1), []);

        Assert.Equal("2025-12", january.PreviousKey);
        Assert.Equal("2027-01", december.NextKey);
        Assert.Equal("2026-01", january.Key);
    }
}
