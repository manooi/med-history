using System.Globalization;
using MedHistory.Models;
using MedHistory.Services;

namespace MedHistory.Tests;

public class ReportRulesTests
{
    private static readonly DateOnly August = new(2026, 8, 1);

    private static readonly DateOnly Day = new(2026, 8, 14);

    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private static readonly CultureInfo Thai = CultureInfo.GetCultureInfo("th-TH");

    private static ReportAllocation Allocation(int id, MedSlots slots, DateOnly? day = null) =>
        new(id, day ?? Day, slots);

    /// <summary>A tick as it comes off an entry: entry id, allocation, slot name.</summary>
    private static ChecklistTick Tick(int entryId, int? allocationId, string? slot) =>
        new(entryId, allocationId, slot);

    private static ReportDay OneDay(
        IEnumerable<ReportAllocation> allocations, IEnumerable<ChecklistTick> ticks) =>
        Assert.Single(ReportRules.TallyDays(allocations, ticks));

    private static IReadOnlyList<ReportDay?> Cells(IEnumerable<ReportWeek> weeks) =>
        weeks.SelectMany(week => week.Days).ToList();

    // ---- TallyDays: planned ----

    [Fact]
    public void TallyDays_CountsOneDosePerSlot()
    {
        var day = OneDay([Allocation(1, MedSlots.Morning | MedSlots.Bedtime)], []);

        Assert.Equal(2, day.Planned);
    }

    [Fact]
    public void TallyDays_MultipleAllocationsOnOneDay_SumsTheirSlots()
    {
        // Three slots plus one — the report counts doses, not medications.
        var day = OneDay(
            [
                Allocation(1, MedSlots.Morning | MedSlots.Noon | MedSlots.Evening),
                Allocation(2, MedSlots.Bedtime)
            ],
            []);

        Assert.Equal(4, day.Planned);
        Assert.Equal(Day, day.Day);
    }

    [Fact]
    public void TallyDays_DaysWithoutAllocations_ProduceNoRow()
    {
        // Filling in the empty days is BuildWeeks' job — only it knows the month's length.
        var days = ReportRules.TallyDays(
            [
                Allocation(1, MedSlots.Morning, new DateOnly(2026, 8, 1)),
                Allocation(2, MedSlots.Morning, new DateOnly(2026, 8, 3))
            ],
            []);

        Assert.Equal([new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 3)], days.Select(d => d.Day));
    }

    [Fact]
    public void TallyDays_ReturnsDaysInDayOrder()
    {
        var days = ReportRules.TallyDays(
            [
                Allocation(1, MedSlots.Morning, new DateOnly(2026, 8, 9)),
                Allocation(2, MedSlots.Morning, new DateOnly(2026, 8, 2)),
                Allocation(3, MedSlots.Morning, new DateOnly(2026, 8, 20))
            ],
            []);

        Assert.Equal(
            [new DateOnly(2026, 8, 2), new DateOnly(2026, 8, 9), new DateOnly(2026, 8, 20)],
            days.Select(d => d.Day));
    }

    [Fact]
    public void TallyDays_NoAllocationsAtAll_ReturnsEmpty()
    {
        Assert.Empty(ReportRules.TallyDays([], [Tick(1, 1, "Morning")]));
    }

    // ---- TallyDays: ticked ----

    [Fact]
    public void TallyDays_TickedSlot_Counts()
    {
        var day = OneDay(
            [Allocation(1, MedSlots.Morning | MedSlots.Bedtime)],
            [Tick(10, 1, "Morning")]);

        Assert.Equal(2, day.Planned);
        Assert.Equal(1, day.Ticked);
    }

    [Fact]
    public void TallyDays_TicksAcrossAllocations_CountedAgainstTheirOwn()
    {
        var day = OneDay(
            [Allocation(1, MedSlots.Morning), Allocation(2, MedSlots.Morning | MedSlots.Evening)],
            [Tick(10, 1, "Morning"), Tick(11, 2, "Evening")]);

        Assert.Equal(3, day.Planned);
        Assert.Equal(2, day.Ticked);
    }

    [Fact]
    public void TallyDays_TwoTicksOnOneSlot_CountOnce()
    {
        // Ticking is a no-op on an already-ticked slot, but a slot with two entries behind it
        // must still be one dose — otherwise a day could read 3/2.
        var day = OneDay(
            [Allocation(1, MedSlots.Morning)],
            [Tick(10, 1, "Morning"), Tick(11, 1, "Morning")]);

        Assert.Equal(1, day.Planned);
        Assert.Equal(1, day.Ticked);
    }

    [Fact]
    public void TallyDays_TickForSlotTheAllocationDoesNotPlan_Ignored()
    {
        // An edit that dropped the evening slot leaves its entry behind; the plan is what counts.
        var day = OneDay(
            [Allocation(1, MedSlots.Morning)],
            [Tick(10, 1, "Morning"), Tick(11, 1, "Evening")]);

        Assert.Equal(1, day.Planned);
        Assert.Equal(1, day.Ticked);
    }

    [Theory]
    [InlineData("Elevenses")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TallyDays_UnparseableSlot_Ignored(string? slot)
    {
        var day = OneDay([Allocation(1, MedSlots.Morning)], [Tick(10, 1, slot)]);

        Assert.Equal(0, day.Ticked);
    }

    [Theory]
    [InlineData("morning")]
    [InlineData("MORNING")]
    [InlineData(" Morning ")]
    public void TallyDays_SlotNameCasingAndPadding_StillTicks(string slot)
    {
        // Same tolerance ChecklistRules.FindTick has — the name comes off a stored column.
        var day = OneDay([Allocation(1, MedSlots.Morning)], [Tick(10, 1, slot)]);

        Assert.Equal(1, day.Ticked);
    }

    [Fact]
    public void TallyDays_TickLinkedToAnAllocationThatIsNotThere_Ignored()
    {
        // A dangling link — the allocation was deleted, the dose stayed. It ticks nothing.
        var day = OneDay([Allocation(1, MedSlots.Morning)], [Tick(10, 99, "Morning")]);

        Assert.Equal(0, day.Ticked);
    }

    [Fact]
    public void TallyDays_TickWithNoAllocationId_Ignored()
    {
        // A hand-typed Med entry: a real dose, but not one the checklist speaks for.
        var day = OneDay([Allocation(1, MedSlots.Morning)], [Tick(10, null, "Morning")]);

        Assert.Equal(0, day.Ticked);
    }

    [Fact]
    public void TallyDays_TicksLandOnTheAllocationsDay_NotTheEntrys()
    {
        // The tick carries no date of its own here: it belongs to the day it was ticked for,
        // which is the whole reason the report reads ticks by link rather than by timestamp.
        var days = ReportRules.TallyDays(
            [Allocation(1, MedSlots.Morning, new DateOnly(2026, 8, 4))],
            [Tick(10, 1, "Morning")]);

        var day = Assert.Single(days);

        Assert.Equal(new DateOnly(2026, 8, 4), day.Day);
        Assert.Equal(1, day.Ticked);
    }

    [Fact]
    public void TallyDays_EveryDoseTicked_TickedEqualsPlanned()
    {
        var day = OneDay(
            [Allocation(1, MedSlots.Morning | MedSlots.Noon), Allocation(2, MedSlots.Bedtime)],
            [Tick(10, 1, "Morning"), Tick(11, 1, "Noon"), Tick(12, 2, "Bedtime")]);

        Assert.Equal(3, day.Planned);
        Assert.Equal(3, day.Ticked);
    }

    // ---- ReportDay.State ----

    [Fact]
    public void State_NothingPlanned_IsNoPlan()
    {
        // A day with nothing to do must not read as a day of missed doses.
        Assert.Equal(DayProgress.NoPlan, new ReportDay(Day, 0, 0).State);
    }

    [Fact]
    public void State_PlannedAndNothingTicked_IsNone()
    {
        Assert.Equal(DayProgress.None, new ReportDay(Day, 3, 0).State);
    }

    [Fact]
    public void State_SomeTicked_IsPartial()
    {
        Assert.Equal(DayProgress.Partial, new ReportDay(Day, 3, 2).State);
    }

    [Fact]
    public void State_AllTicked_IsFull()
    {
        Assert.Equal(DayProgress.Full, new ReportDay(Day, 3, 3).State);
    }

    [Fact]
    public void State_MoreTickedThanPlanned_IsFull()
    {
        // The plan shrank under doses already logged; the day is still done, not partly done.
        Assert.Equal(DayProgress.Full, new ReportDay(Day, 1, 2).State);
    }

    [Fact]
    public void ProgressLabel_NothingPlanned_IsEmpty()
    {
        Assert.Equal(string.Empty, new ReportDay(Day, 0, 0).ProgressLabel);
    }

    [Fact]
    public void ProgressLabel_Planned_IsTickedOverPlanned()
    {
        Assert.Equal("2/3", new ReportDay(Day, 3, 2).ProgressLabel);
    }

    // ---- BuildWeeks: shape ----

    [Fact]
    public void BuildWeeks_EveryWeekIsSevenCells()
    {
        foreach (var month in new[] { new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 1), new DateOnly(2024, 2, 1) })
        {
            Assert.All(ReportRules.BuildWeeks(month, []), week => Assert.Equal(7, week.Days.Count));
        }
    }

    [Fact]
    public void BuildWeeks_FebruaryInALeapYear_HasTwentyNineDays()
    {
        // 1 Feb 2024 is a Thursday: three blanks before it, 29 days, five rows.
        var weeks = ReportRules.BuildWeeks(new DateOnly(2024, 2, 1), []);
        var cells = Cells(weeks);

        Assert.Equal(5, weeks.Count);
        Assert.Equal(3, cells.TakeWhile(cell => cell is null).Count());
        Assert.Equal(29, cells.Count(cell => cell is not null));
        Assert.Equal(new DateOnly(2024, 2, 29), cells.Last(cell => cell is not null)!.Value.Day);
    }

    [Fact]
    public void BuildWeeks_FebruaryInACommonYear_HasTwentyEightDays()
    {
        var cells = Cells(ReportRules.BuildWeeks(new DateOnly(2026, 2, 1), []));

        Assert.Equal(28, cells.Count(cell => cell is not null));
        Assert.Equal(new DateOnly(2026, 2, 28), cells.Last(cell => cell is not null)!.Value.Day);
    }

    [Fact]
    public void BuildWeeks_MonthStartingOnMonday_HasNoBlanks()
    {
        // 1 Feb 2021 is a Monday and February 2021 has 28 days: four rows, dead flush.
        var weeks = ReportRules.BuildWeeks(new DateOnly(2021, 2, 1), []);

        Assert.Equal(4, weeks.Count);
        Assert.All(Cells(weeks), cell => Assert.NotNull(cell));
    }

    [Fact]
    public void BuildWeeks_ThirtyOneDayMonthStartingOnSunday_SpillsToSixRows()
    {
        // 1 Mar 2026 is a Sunday — Monday-first, that is the last column of the first row, so
        // six blanks lead and the month needs a sixth row. A Sunday-first grid would show one.
        var weeks = ReportRules.BuildWeeks(new DateOnly(2026, 3, 1), []);
        var cells = Cells(weeks);

        Assert.Equal(6, weeks.Count);
        Assert.Equal(6, cells.TakeWhile(cell => cell is null).Count());
        Assert.Equal(new DateOnly(2026, 3, 1), weeks[0].Days[6]!.Value.Day);
        Assert.Equal(31, cells.Count(cell => cell is not null));
    }

    [Fact]
    public void BuildWeeks_FirstColumnIsAlwaysMonday()
    {
        foreach (var month in new[] { new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 1), new DateOnly(2026, 12, 1) })
        {
            Assert.All(
                ReportRules.BuildWeeks(month, []).SelectMany(week => week.Days.Take(1)),
                cell => Assert.True(cell is null || cell.Value.Day.DayOfWeek == DayOfWeek.Monday));
        }
    }

    [Fact]
    public void BuildWeeks_December_HoldsNoDayOfTheNextYear()
    {
        var cells = Cells(ReportRules.BuildWeeks(new DateOnly(2026, 12, 1), []));

        Assert.Equal(31, cells.Count(cell => cell is not null));
        Assert.All(
            cells.Where(cell => cell is not null),
            cell => Assert.Equal(new DateOnly(2026, 12, 1), ReportRules.FirstOfMonth(cell!.Value.Day)));
    }

    [Fact]
    public void BuildWeeks_DaysRunInOrderWithNoGaps()
    {
        var days = Cells(ReportRules.BuildWeeks(new DateOnly(2026, 8, 1), []))
            .Where(cell => cell is not null)
            .Select(cell => cell!.Value.Day)
            .ToList();

        Assert.Equal(Enumerable.Range(1, 31).Select(n => new DateOnly(2026, 8, n)), days);
    }

    [Fact]
    public void BuildWeeks_AnyDayOfTheMonth_LaysOutTheWholeMonth()
    {
        // The caller passes whatever day it has; only the month it falls in matters.
        Assert.Equal(
            Cells(ReportRules.BuildWeeks(new DateOnly(2026, 8, 1), [])).Select(cell => cell?.Day),
            Cells(ReportRules.BuildWeeks(new DateOnly(2026, 8, 23), [])).Select(cell => cell?.Day));
    }

    // ---- BuildWeeks: filling ----

    [Fact]
    public void BuildWeeks_DayWithATally_CarriesIt()
    {
        var weeks = ReportRules.BuildWeeks(August, [new ReportDay(new DateOnly(2026, 8, 4), 3, 2)]);

        var cell = Cells(weeks).Single(c => c?.Day == new DateOnly(2026, 8, 4))!.Value;

        Assert.Equal(3, cell.Planned);
        Assert.Equal(2, cell.Ticked);
        Assert.Equal(DayProgress.Partial, cell.State);
    }

    [Fact]
    public void BuildWeeks_DayWithNoTally_IsAnEmptyPlan()
    {
        var weeks = ReportRules.BuildWeeks(August, [new ReportDay(new DateOnly(2026, 8, 4), 3, 2)]);

        var cell = Cells(weeks).Single(c => c?.Day == new DateOnly(2026, 8, 5))!.Value;

        Assert.Equal(0, cell.Planned);
        Assert.Equal(DayProgress.NoPlan, cell.State);
    }

    [Fact]
    public void BuildWeeks_TalliesOutsideTheMonth_AreNotDrawn()
    {
        var cells = Cells(ReportRules.BuildWeeks(August, [new ReportDay(new DateOnly(2026, 9, 1), 3, 3)]));

        Assert.DoesNotContain(cells, cell => cell?.Day == new DateOnly(2026, 9, 1));
        Assert.All(cells.Where(cell => cell is not null), cell => Assert.Equal(0, cell!.Value.Planned));
    }

    // ---- LeadingBlanks ----

    [Theory]
    [InlineData(2026, 6, 1, 0)]  // Monday
    [InlineData(2026, 9, 1, 1)]  // Tuesday
    [InlineData(2026, 4, 1, 2)]  // Wednesday
    [InlineData(2026, 1, 1, 3)]  // Thursday
    [InlineData(2026, 5, 1, 4)]  // Friday
    [InlineData(2026, 8, 1, 5)]  // Saturday
    [InlineData(2026, 2, 1, 6)]  // Sunday — the last column, not the first
    public void LeadingBlanks_CountsBackToMonday(int year, int month, int day, int expected)
    {
        Assert.Equal(expected, ReportRules.LeadingBlanks(new DateOnly(year, month, day)));
    }

    // ---- BuildMonth ----

    [Fact]
    public void BuildMonth_TotalsEveryDoseInTheMonth()
    {
        var report = ReportRules.BuildMonth(
            August,
            [
                Allocation(1, MedSlots.Morning | MedSlots.Bedtime, new DateOnly(2026, 8, 1)),
                Allocation(2, MedSlots.Noon, new DateOnly(2026, 8, 1)),
                Allocation(3, MedSlots.Morning | MedSlots.Evening, new DateOnly(2026, 8, 20))
            ],
            [Tick(10, 1, "Morning"), Tick(11, 2, "Noon"), Tick(12, 3, "Evening")]);

        Assert.Equal(5, report.Planned);
        Assert.Equal(3, report.Ticked);
    }

    [Fact]
    public void BuildMonth_AllocationsInOtherMonths_CountForNothing()
    {
        // The caller may over-fetch; the month's numbers must not bend when it does.
        var report = ReportRules.BuildMonth(
            August,
            [
                Allocation(1, MedSlots.Morning, new DateOnly(2026, 8, 3)),
                Allocation(2, MedSlots.Morning | MedSlots.Noon, new DateOnly(2026, 7, 31)),
                Allocation(3, MedSlots.Bedtime, new DateOnly(2026, 9, 1))
            ],
            [Tick(10, 2, "Morning"), Tick(11, 3, "Bedtime")]);

        Assert.Equal(1, report.Planned);
        Assert.Equal(0, report.Ticked);
        Assert.DoesNotContain(Cells(report.Weeks), cell => cell?.Day.Month != 8 && cell is not null);
    }

    [Fact]
    public void BuildMonth_NothingPlanned_IsAWholeMonthOfEmptyDays()
    {
        var report = ReportRules.BuildMonth(August, [], []);

        Assert.Equal(0, report.Planned);
        Assert.Equal(0, report.Ticked);
        Assert.All(
            Cells(report.Weeks).Where(cell => cell is not null),
            cell => Assert.Equal(DayProgress.NoPlan, cell!.Value.State));
    }

    [Fact]
    public void BuildMonth_PlacesEachDaysTallyOnItsOwnCell()
    {
        var report = ReportRules.BuildMonth(
            August,
            [Allocation(1, MedSlots.Morning | MedSlots.Evening, new DateOnly(2026, 8, 12))],
            [Tick(10, 1, "Evening")]);

        var cell = Cells(report.Weeks).Single(c => c?.Day == new DateOnly(2026, 8, 12))!.Value;

        Assert.Equal("1/2", cell.ProgressLabel);
        Assert.Equal(DayProgress.Partial, cell.State);
    }

    [Fact]
    public void BuildMonth_AnyDayOfTheMonth_ReportsTheWholeMonth()
    {
        var report = ReportRules.BuildMonth(new DateOnly(2026, 8, 23), [], []);

        Assert.Equal(August, report.FirstDay);
    }

    [Fact]
    public void BuildMonth_ProgressKey_IsTheFractionTemplate()
    {
        // The key, not the copy — the view looks it up and formats Ticked then Planned into it.
        // ResourceLayoutTests is what checks the key is really in the med report's .resx.
        var report = ReportRules.BuildMonth(
            August,
            [Allocation(1, MedSlots.Morning | MedSlots.Evening, new DateOnly(2026, 8, 12))],
            [Tick(10, 1, "Evening")]);

        Assert.Equal("{0}/{1} doses", report.ProgressKey);
        Assert.Equal(1, report.Ticked);
        Assert.Equal(2, report.Planned);
    }

    [Fact]
    public void BuildMonth_NothingPlanned_ProgressKeySaysSo()
    {
        Assert.Equal("nothing planned", ReportRules.BuildMonth(August, [], []).ProgressKey);
    }

    // ---- Month keys and labels ----

    [Fact]
    public void MonthKey_IsTheYearAndMonth()
    {
        Assert.Equal("2026-08", ReportRules.MonthKey(August));
    }

    [Fact]
    public void MonthKey_UnderThaiAmbientCulture_StaysGregorian()
    {
        // The month key is a URL segment and an <input type="month"> value. A Buddhist-era
        // "2569-08" would parse back as the year 2569 rather than fail, so the report would
        // quietly page through a century it has no data for.
        using var culture = new CultureScope("th-TH");

        Assert.Equal("2026-08", ReportRules.MonthKey(August));
    }

    [Fact]
    public void TryParseMonth_UnderThaiAmbientCulture_RoundTripsTheKey()
    {
        using var culture = new CultureScope("th-TH");

        Assert.True(ReportRules.TryParseMonth(ReportRules.MonthKey(August), out var month));
        Assert.Equal(August, month);
    }

    [Fact]
    public void MonthLabel_InEnglish_NamesTheMonth()
    {
        Assert.Equal("August 2026", ReportRules.MonthLabel(August, English));
    }

    [Fact]
    public void MonthLabel_InThai_ReadsTheBuddhistEraYear()
    {
        var label = ReportRules.MonthLabel(August, Thai);

        Assert.Contains("2569", label);
        Assert.DoesNotContain("2026", label);
        Assert.DoesNotContain("August", label);
    }

    [Fact]
    public void MonthLabel_TakesTheCultureItIsGiven_NotTheAmbientOne()
    {
        // The point of the parameter: a page rendered in English stays English even when the
        // thread it renders on says otherwise.
        using var culture = new CultureScope("th-TH");

        Assert.Equal("August 2026", ReportRules.MonthLabel(August, English));
    }

    // ---- Weekday headings ----

    [Fact]
    public void WeekdayLabels_InEnglish_RunMondayFirst()
    {
        Assert.Equal(
            new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" },
            ReportRules.WeekdayLabels(English));
    }

    [Fact]
    public void WeekdayLabels_InThai_AreThaiAndStillRunMondayFirst()
    {
        var labels = ReportRules.WeekdayLabels(Thai);

        Assert.Equal(ReportRules.DaysPerWeek, labels.Count);
        Assert.Equal(Thai.DateTimeFormat.AbbreviatedDayNames[(int)DayOfWeek.Monday], labels[0]);
        Assert.Equal(Thai.DateTimeFormat.AbbreviatedDayNames[(int)DayOfWeek.Sunday], labels[^1]);
        Assert.DoesNotContain("Mon", labels);
    }

    [Fact]
    public void PreviousAndNextKeys_CrossTheYearBoundary()
    {
        var january = ReportRules.BuildMonth(new DateOnly(2026, 1, 1), [], []);
        var december = ReportRules.BuildMonth(new DateOnly(2026, 12, 1), [], []);

        Assert.Equal("2025-12", january.PreviousKey);
        Assert.Equal("2027-01", december.NextKey);
        Assert.Equal("2026-01", january.Key);
    }

    [Fact]
    public void TryParseMonth_WellFormed_IsTheFirstOfThatMonth()
    {
        Assert.True(ReportRules.TryParseMonth("2026-08", out var month));
        Assert.Equal(August, month);
    }

    [Fact]
    public void TryParseMonth_RoundTripsMonthKey()
    {
        Assert.True(ReportRules.TryParseMonth(ReportRules.MonthKey(August), out var month));
        Assert.Equal(August, month);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-8")]      // month must be two digits
    [InlineData("2026-13")]     // no such month
    [InlineData("2026-00")]
    [InlineData("2026-08-01")]  // a day, not a month
    [InlineData("2026")]
    [InlineData(" 2026-08")]
    [InlineData("2026-08 ")]
    [InlineData("August 2026")]
    [InlineData("garbage")]
    public void TryParseMonth_Malformed_False(string? value)
    {
        // Nothing is coerced into a nearby month the reader did not ask for.
        Assert.False(ReportRules.TryParseMonth(value, out _));
    }

    // ---- FirstOfMonth ----

    [Fact]
    public void FirstOfMonth_KeepsYearAndMonth()
    {
        Assert.Equal(August, ReportRules.FirstOfMonth(new DateOnly(2026, 8, 31)));
    }

    [Fact]
    public void FirstOfMonth_OnTheFirst_IsItself()
    {
        Assert.Equal(August, ReportRules.FirstOfMonth(August));
    }
}
