using MedHistory.Services;

namespace MedHistory.Tests;

public class TypeReportRulesTests
{
    // ---- PageCount ----

    [Fact]
    public void PageCount_ZeroDays_IsZero()
    {
        // A type with nothing logged has no page to be "page 1" of — that is what lets
        // ClampPage tell an empty type apart from an out-of-range request into a real one.
        Assert.Equal(0, TypeReportRules.PageCount(0));
    }

    [Fact]
    public void PageCount_ExactlyOnePageWorth_IsOnePage()
    {
        Assert.Equal(1, TypeReportRules.PageCount(TypeReportRules.PerPage));
    }

    [Fact]
    public void PageCount_OneDayOverAPage_IsTwoPages()
    {
        Assert.Equal(2, TypeReportRules.PageCount(TypeReportRules.PerPage + 1));
    }

    [Fact]
    public void PageCount_OneDay_IsOnePage()
    {
        Assert.Equal(1, TypeReportRules.PageCount(1));
    }

    [Fact]
    public void PageCount_BigDayCount_DividesEvenly()
    {
        // 10,000 days at 30/page: not evenly divisible, so it rounds up.
        Assert.Equal(334, TypeReportRules.PageCount(10_000));
    }

    [Fact]
    public void PageCount_NegativeDayCount_IsZero()
    {
        // Not a real input, but should not throw or go negative.
        Assert.Equal(0, TypeReportRules.PageCount(-5));
    }

    // ---- ClampPage ----

    [Fact]
    public void ClampPage_ZeroPageCount_ClampsToOne()
    {
        // Nothing to redirect toward except the type's own (empty) first page.
        Assert.Equal(1, TypeReportRules.ClampPage(1, 0));
        Assert.Equal(1, TypeReportRules.ClampPage(5, 0));
        Assert.Equal(1, TypeReportRules.ClampPage(0, 0));
    }

    [Fact]
    public void ClampPage_WithinRange_IsUnchanged()
    {
        Assert.Equal(2, TypeReportRules.ClampPage(2, 3));
    }

    [Fact]
    public void ClampPage_BelowOne_ClampsUpToOne()
    {
        Assert.Equal(1, TypeReportRules.ClampPage(0, 3));
        Assert.Equal(1, TypeReportRules.ClampPage(-5, 3));
    }

    [Fact]
    public void ClampPage_AboveCount_ClampsDownToLastPage()
    {
        Assert.Equal(3, TypeReportRules.ClampPage(99, 3));
    }

    [Fact]
    public void ClampPage_ExactlyPageCount_IsUnchanged()
    {
        Assert.Equal(3, TypeReportRules.ClampPage(3, 3));
    }

    // ---- SelectDays ----

    private static readonly IReadOnlyList<int> ThirtyOneDays =
        Enumerable.Range(1, 31).Reverse().ToList(); // newest (31) first, oldest (1) last

    [Fact]
    public void SelectDays_FirstPage_IsNewestDaysFirst()
    {
        var page = TypeReportRules.SelectDays(ThirtyOneDays, 1);

        Assert.Equal(30, page.Count);
        Assert.Equal(31, page[0]);
        Assert.Equal(2, page[^1]);
    }

    [Fact]
    public void SelectDays_LastPage_IsThePartialRemainder()
    {
        var page = TypeReportRules.SelectDays(ThirtyOneDays, 2);

        Assert.Equal([1], page);
    }

    [Fact]
    public void SelectDays_ExactlyOnePageWorth_OnePageHoldsAllOfIt()
    {
        IReadOnlyList<int> days = Enumerable.Range(1, TypeReportRules.PerPage).Reverse().ToList();

        var page = TypeReportRules.SelectDays(days, 1);

        Assert.Equal(TypeReportRules.PerPage, page.Count);
        Assert.Equal(days, page);
    }

    [Fact]
    public void SelectDays_EmptyList_IsEmpty()
    {
        Assert.Empty(TypeReportRules.SelectDays<int>([], 1));
    }

    [Fact]
    public void SelectDays_PageBeyondTheData_IsEmpty()
    {
        // Callers are expected to pass an already-clamped page, but this must not throw if one
        // slips through unclamped.
        Assert.Empty(TypeReportRules.SelectDays(ThirtyOneDays, 5));
    }

    [Fact]
    public void SelectDays_MiddlePage_SlicesBetweenTheNeighbours()
    {
        IReadOnlyList<int> days = Enumerable.Range(1, 65).Reverse().ToList(); // 65..1

        var first = TypeReportRules.SelectDays(days, 1);
        var second = TypeReportRules.SelectDays(days, 2);
        var third = TypeReportRules.SelectDays(days, 3);

        Assert.Equal(65, first[0]);
        Assert.Equal(36, first[^1]);
        Assert.Equal(35, second[0]);
        Assert.Equal(6, second[^1]);
        Assert.Equal([5, 4, 3, 2, 1], third);
    }

    // ---- GroupByDayDescending ----

    private readonly record struct Row(int Id, DateOnly Day, DateTimeOffset OccurredAt);

    private static DateTimeOffset At(int y, int m, int d, int hh, int mm) =>
        new(y, m, d, hh, mm, 0, TimeSpan.Zero);

    [Fact]
    public void GroupByDayDescending_OrdersDaysNewestFirst()
    {
        var rows = new[]
        {
            new Row(1, new DateOnly(2026, 8, 1), At(2026, 8, 1, 9, 0)),
            new Row(2, new DateOnly(2026, 8, 10), At(2026, 8, 10, 9, 0)),
            new Row(3, new DateOnly(2026, 8, 5), At(2026, 8, 5, 9, 0))
        };

        var groups = TypeReportRules.GroupByDayDescending(rows, r => r.Day, r => r.OccurredAt);

        Assert.Equal(
            [new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 5), new DateOnly(2026, 8, 1)],
            groups.Select(g => g.Key));
    }

    [Fact]
    public void GroupByDayDescending_EntriesWithinADay_AreAscendingByTime()
    {
        var rows = new[]
        {
            new Row(1, new DateOnly(2026, 8, 1), At(2026, 8, 1, 18, 0)),
            new Row(2, new DateOnly(2026, 8, 1), At(2026, 8, 1, 9, 0)),
            new Row(3, new DateOnly(2026, 8, 1), At(2026, 8, 1, 12, 0))
        };

        var group = Assert.Single(TypeReportRules.GroupByDayDescending(rows, r => r.Day, r => r.OccurredAt));

        Assert.Equal([2, 3, 1], group.Select(r => r.Id));
    }

    [Fact]
    public void GroupByDayDescending_MultipleDaysEachKeepTheirOwnEntriesAscending()
    {
        var rows = new[]
        {
            new Row(1, new DateOnly(2026, 8, 5), At(2026, 8, 5, 20, 0)),
            new Row(2, new DateOnly(2026, 8, 1), At(2026, 8, 1, 8, 0)),
            new Row(3, new DateOnly(2026, 8, 5), At(2026, 8, 5, 7, 0)),
            new Row(4, new DateOnly(2026, 8, 1), At(2026, 8, 1, 22, 0))
        };

        var groups = TypeReportRules.GroupByDayDescending(rows, r => r.Day, r => r.OccurredAt).ToList();

        Assert.Equal(2, groups.Count);
        Assert.Equal(new DateOnly(2026, 8, 5), groups[0].Key);
        Assert.Equal([3, 1], groups[0].Select(r => r.Id));
        Assert.Equal(new DateOnly(2026, 8, 1), groups[1].Key);
        Assert.Equal([2, 4], groups[1].Select(r => r.Id));
    }

    [Fact]
    public void GroupByDayDescending_NoEntries_IsEmpty()
    {
        Assert.Empty(TypeReportRules.GroupByDayDescending(Array.Empty<Row>(), r => r.Day, r => r.OccurredAt));
    }

    [Fact]
    public void GroupByDayDescending_OneDayOneEntry_IsOneGroupOfOne()
    {
        var rows = new[] { new Row(1, new DateOnly(2026, 8, 1), At(2026, 8, 1, 9, 0)) };

        var group = Assert.Single(TypeReportRules.GroupByDayDescending(rows, r => r.Day, r => r.OccurredAt));

        Assert.Equal(new DateOnly(2026, 8, 1), group.Key);
        Assert.Equal([1], group.Select(r => r.Id));
    }
}
