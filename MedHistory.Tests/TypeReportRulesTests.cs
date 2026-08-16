using MedHistory.Services;

namespace MedHistory.Tests;

public class TypeReportRulesTests
{
    // Display order as AllTypeNamesAsync hands it over: built-ins first, then user types.
    private static readonly IReadOnlyList<string> AllTypes =
        ["Symptom", "Bleeding", "Med", "Cough", "Meal", "Note", "Dizziness"];

    // ---- CanonicalizeTypes ----

    [Fact]
    public void CanonicalizeTypes_NothingRequested_IsEmpty()
    {
        Assert.Empty(TypeReportRules.CanonicalizeTypes([], AllTypes));
    }

    [Fact]
    public void CanonicalizeTypes_KnownNames_AreKept()
    {
        Assert.Equal(["Symptom", "Cough"], TypeReportRules.CanonicalizeTypes(["Symptom", "Cough"], AllTypes));
    }

    [Fact]
    public void CanonicalizeTypes_UnknownName_IsDropped()
    {
        // A renamed or deleted type in an old bookmark drops out; the rest of the URL still works.
        Assert.Equal(["Med"], TypeReportRules.CanonicalizeTypes(["Med", "Pill"], AllTypes));
    }

    [Fact]
    public void CanonicalizeTypes_OnlyUnknownNames_IsEmpty()
    {
        Assert.Empty(TypeReportRules.CanonicalizeTypes(["Pill", "nope"], AllTypes));
    }

    [Fact]
    public void CanonicalizeTypes_DuplicateNames_CollapseToOne()
    {
        Assert.Equal(["Meal"], TypeReportRules.CanonicalizeTypes(["Meal", "Meal", "meal"], AllTypes));
    }

    [Fact]
    public void CanonicalizeTypes_WrongCasing_ComesBackAsStored()
    {
        // Entry.Type is compared ordinal, so a hand-typed URL must not miss a type by casing.
        Assert.Equal(["Bleeding", "Dizziness"], TypeReportRules.CanonicalizeTypes(["bleeding", "DIZZINESS"], AllTypes));
    }

    [Fact]
    public void CanonicalizeTypes_RequestOrder_IsIgnoredForDisplayOrder()
    {
        // One selection, one URL: either request order canonicalises to the same list.
        Assert.Equal(
            ["Symptom", "Cough", "Dizziness"],
            TypeReportRules.CanonicalizeTypes(["Dizziness", "Cough", "Symptom"], AllTypes));
        Assert.Equal(
            TypeReportRules.CanonicalizeTypes(["Cough", "Symptom"], AllTypes),
            TypeReportRules.CanonicalizeTypes(["Symptom", "Cough"], AllTypes));
    }

    [Fact]
    public void CanonicalizeTypes_NullName_IsDropped()
    {
        // ?types=&types=Meal binds a null/empty element rather than nothing at all.
        Assert.Equal(["Meal"], TypeReportRules.CanonicalizeTypes([null, "Meal"], AllTypes));
    }

    // ---- ToggleType ----

    [Fact]
    public void ToggleType_NotSelected_IsAdded()
    {
        Assert.Equal(["Symptom", "Cough"], TypeReportRules.ToggleType(["Symptom"], "Cough", AllTypes));
    }

    [Fact]
    public void ToggleType_AddedType_LandsInDisplayOrderNotAtTheEnd()
    {
        Assert.Equal(["Symptom", "Cough"], TypeReportRules.ToggleType(["Cough"], "Symptom", AllTypes));
    }

    [Fact]
    public void ToggleType_AlreadySelected_IsRemoved()
    {
        Assert.Equal(["Symptom", "Meal"], TypeReportRules.ToggleType(["Symptom", "Cough", "Meal"], "Cough", AllTypes));
    }

    [Fact]
    public void ToggleType_LastSelectedTypeOut_LeavesEmpty()
    {
        // Which is the bare selector page again — untickable back to nothing.
        Assert.Empty(TypeReportRules.ToggleType(["Cough"], "Cough", AllTypes));
    }

    [Fact]
    public void ToggleType_MatchesSelectionCaseInsensitively()
    {
        Assert.Empty(TypeReportRules.ToggleType(["Cough"], "cough", AllTypes));
    }

    [Fact]
    public void ToggleType_UnknownType_ChangesNothing()
    {
        // Nothing in the selector can produce this, but a toggle can never invent a type either.
        Assert.Equal(["Symptom"], TypeReportRules.ToggleType(["Symptom"], "Pill", AllTypes));
    }

    // ---- NeedsCanonicalRedirect ----

    [Fact]
    public void NeedsCanonicalRedirect_AlreadyCanonical_IsFalse()
    {
        string[] requested = ["Symptom", "Cough"];

        Assert.False(TypeReportRules.NeedsCanonicalRedirect(
            requested, TypeReportRules.CanonicalizeTypes(requested, AllTypes)));
    }

    [Fact]
    public void NeedsCanonicalRedirect_NothingRequested_IsFalse()
    {
        Assert.False(TypeReportRules.NeedsCanonicalRedirect([], TypeReportRules.CanonicalizeTypes([], AllTypes)));
    }

    [Fact]
    public void NeedsCanonicalRedirect_UnknownNameDropped_IsTrue()
    {
        string[] requested = ["Symptom", "Pill"];

        Assert.True(TypeReportRules.NeedsCanonicalRedirect(
            requested, TypeReportRules.CanonicalizeTypes(requested, AllTypes)));
    }

    [Fact]
    public void NeedsCanonicalRedirect_DuplicateCollapsed_IsTrue()
    {
        string[] requested = ["Meal", "Meal"];

        Assert.True(TypeReportRules.NeedsCanonicalRedirect(
            requested, TypeReportRules.CanonicalizeTypes(requested, AllTypes)));
    }

    [Fact]
    public void NeedsCanonicalRedirect_WrongCasing_IsTrue()
    {
        string[] requested = ["cough"];

        Assert.True(TypeReportRules.NeedsCanonicalRedirect(
            requested, TypeReportRules.CanonicalizeTypes(requested, AllTypes)));
    }

    [Fact]
    public void NeedsCanonicalRedirect_WrongOrder_IsTrue()
    {
        string[] requested = ["Cough", "Symptom"];

        Assert.True(TypeReportRules.NeedsCanonicalRedirect(
            requested, TypeReportRules.CanonicalizeTypes(requested, AllTypes)));
    }

    [Fact]
    public void NeedsCanonicalRedirect_NoSortAsked_IsFalse()
    {
        // The default order is spelled by leaving the parameter off — the address every link
        // predating the toggle already uses.
        Assert.False(TypeReportRules.NeedsCanonicalRedirect(["Cough"], ["Cough"], null));
    }

    [Fact]
    public void NeedsCanonicalRedirect_NewestSort_IsFalse()
    {
        Assert.False(TypeReportRules.NeedsCanonicalRedirect(["Cough"], ["Cough"], "newest"));
    }

    [Fact]
    public void NeedsCanonicalRedirect_OldestSort_IsTrue()
    {
        // Spelling the default out loud is a second address for one page.
        Assert.True(TypeReportRules.NeedsCanonicalRedirect(["Cough"], ["Cough"], "oldest"));
    }

    [Fact]
    public void NeedsCanonicalRedirect_UnrecognisedSort_IsTrue()
    {
        Assert.True(TypeReportRules.NeedsCanonicalRedirect(["Cough"], ["Cough"], "sideways"));
        Assert.True(TypeReportRules.NeedsCanonicalRedirect(["Cough"], ["Cough"], ""));
    }

    [Fact]
    public void NeedsCanonicalRedirect_WrongTypesAndWrongSort_IsOneAnswer()
    {
        // Both wrong is still one redirect: the answer is about the whole address, and Href
        // rebuilds the whole address.
        string[] requested = ["cough", "Symptom"];
        var canonical = TypeReportRules.CanonicalizeTypes(requested, AllTypes);

        Assert.True(TypeReportRules.NeedsCanonicalRedirect(requested, canonical, "oldest"));
        Assert.Equal(
            "/type-report?types=Symptom&types=Cough",
            TypeReportRules.Href(canonical, 1, TypeReportRules.ParseSort("oldest").Sort));
    }

    // ---- ParseSort ----

    [Fact]
    public void ParseSort_Absent_IsTheDefaultOrderAndCanonical()
    {
        Assert.Equal((TypeReportSort.OldestFirst, true), TypeReportRules.ParseSort(null));
    }

    [Fact]
    public void ParseSort_Newest_IsNewestFirstAndCanonical()
    {
        Assert.Equal((TypeReportSort.NewestFirst, true), TypeReportRules.ParseSort("newest"));
    }

    [Fact]
    public void ParseSort_NewestInWrongCasing_IsUnderstoodButNotCanonical()
    {
        // Understood like a hand-typed type name is, then redirected to its one spelling —
        // rather than thrown away, which would silently flip the reader back to the default.
        Assert.Equal((TypeReportSort.NewestFirst, false), TypeReportRules.ParseSort("NEWEST"));
    }

    [Fact]
    public void ParseSort_Oldest_IsTheDefaultOrderButNotCanonical()
    {
        Assert.Equal((TypeReportSort.OldestFirst, false), TypeReportRules.ParseSort("oldest"));
    }

    [Fact]
    public void ParseSort_UnrecognisedValue_FallsBackWithoutThrowing()
    {
        Assert.Equal((TypeReportSort.OldestFirst, false), TypeReportRules.ParseSort("sideways"));
        Assert.Equal((TypeReportSort.OldestFirst, false), TypeReportRules.ParseSort(""));
        Assert.Equal((TypeReportSort.OldestFirst, false), TypeReportRules.ParseSort("3"));
    }

    // ---- Flip / SortLabel ----

    [Fact]
    public void Flip_GoesBothWays()
    {
        Assert.Equal(TypeReportSort.NewestFirst, TypeReportRules.Flip(TypeReportSort.OldestFirst));
        Assert.Equal(TypeReportSort.OldestFirst, TypeReportRules.Flip(TypeReportSort.NewestFirst));
    }

    [Fact]
    public void SortLabel_NamesTheOrder()
    {
        Assert.Equal("Newest first ↓", TypeReportRules.SortLabel(TypeReportSort.NewestFirst));
        Assert.Equal("Oldest first ↑", TypeReportRules.SortLabel(TypeReportSort.OldestFirst));
    }

    // ---- Href ----

    [Fact]
    public void Href_NoSelection_IsTheBareSelectorPage()
    {
        // No selection has no pages, so a page number never survives clearing one.
        Assert.Equal("/type-report", TypeReportRules.Href([]));
        Assert.Equal("/type-report", TypeReportRules.Href([], 3));
    }

    [Fact]
    public void Href_OneType_IsOneRepeatedParam()
    {
        Assert.Equal("/type-report?types=Cough", TypeReportRules.Href(["Cough"]));
    }

    [Fact]
    public void Href_ManyTypes_RepeatTheParam()
    {
        Assert.Equal("/type-report?types=Symptom&types=Cough", TypeReportRules.Href(["Symptom", "Cough"]));
    }

    [Fact]
    public void Href_FirstPage_LeavesThePageOff()
    {
        Assert.Equal("/type-report?types=Meal", TypeReportRules.Href(["Meal"], 1));
    }

    [Fact]
    public void Href_LaterPage_CarriesEveryType()
    {
        Assert.Equal("/type-report?types=Meal&types=Note&page=4", TypeReportRules.Href(["Meal", "Note"], 4));
    }

    [Fact]
    public void Href_DefaultSort_LeavesTheParamOff()
    {
        Assert.Equal("/type-report?types=Meal", TypeReportRules.Href(["Meal"], 1, TypeReportSort.OldestFirst));
    }

    [Fact]
    public void Href_NewestSort_CarriesIt()
    {
        Assert.Equal(
            "/type-report?types=Meal&sort=newest",
            TypeReportRules.Href(["Meal"], 1, TypeReportSort.NewestFirst));
    }

    [Fact]
    public void Href_NewestSortOnALaterPage_CarriesBoth()
    {
        Assert.Equal(
            "/type-report?types=Meal&types=Note&page=4&sort=newest",
            TypeReportRules.Href(["Meal", "Note"], 4, TypeReportSort.NewestFirst));
    }

    [Fact]
    public void Href_NoSelection_IsTheBareSelectorPageWhateverTheSort()
    {
        // Nothing selected is nothing to order, same reason a page number never survives it.
        Assert.Equal("/type-report", TypeReportRules.Href([], 1, TypeReportSort.NewestFirst));
        Assert.Equal("/type-report", TypeReportRules.Href([], 3, TypeReportSort.NewestFirst));
    }

    [Fact]
    public void Href_UserSuppliedName_IsEscapedIntoTheQuery()
    {
        // Names are free text — a & or a space in one must not split or break the query.
        Assert.Equal("/type-report?types=Aches%20%26%20pains", TypeReportRules.Href(["Aches & pains"]));
    }

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

    // Type only matters where two rows share an instant, so it defaults out of the way.
    private readonly record struct Row(int Id, DateOnly Day, DateTimeOffset OccurredAt, string Type = "Symptom");

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

        var groups = TypeReportRules.GroupByDayDescending(rows, r => r.Day, r => r.OccurredAt, r => r.Type);

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

        var group = Assert.Single(TypeReportRules.GroupByDayDescending(rows, r => r.Day, r => r.OccurredAt, r => r.Type));

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

        var groups = TypeReportRules.GroupByDayDescending(rows, r => r.Day, r => r.OccurredAt, r => r.Type).ToList();

        Assert.Equal(2, groups.Count);
        Assert.Equal(new DateOnly(2026, 8, 5), groups[0].Key);
        Assert.Equal([3, 1], groups[0].Select(r => r.Id));
        Assert.Equal(new DateOnly(2026, 8, 1), groups[1].Key);
        Assert.Equal([2, 4], groups[1].Select(r => r.Id));
    }

    [Fact]
    public void GroupByDayDescending_SameInstantDifferentTypes_TieBreaksByTypeName()
    {
        // A page now spans however many types were selected, so two entries really can share an
        // instant — same answer the day page gives, via EntryRules.OrderEntries.
        var rows = new[]
        {
            new Row(1, new DateOnly(2026, 8, 1), At(2026, 8, 1, 9, 0), "Meal"),
            new Row(2, new DateOnly(2026, 8, 1), At(2026, 8, 1, 9, 0), "Cough"),
            new Row(3, new DateOnly(2026, 8, 1), At(2026, 8, 1, 9, 0), "Bleeding")
        };

        var group = Assert.Single(
            TypeReportRules.GroupByDayDescending(rows, r => r.Day, r => r.OccurredAt, r => r.Type));

        Assert.Equal([3, 2, 1], group.Select(r => r.Id));
    }

    [Fact]
    public void GroupByDayDescending_NewestFirst_IsTheExactReverseWithinEachDay()
    {
        var rows = new[]
        {
            new Row(1, new DateOnly(2026, 8, 1), At(2026, 8, 1, 18, 0)),
            new Row(2, new DateOnly(2026, 8, 1), At(2026, 8, 1, 9, 0)),
            new Row(3, new DateOnly(2026, 8, 1), At(2026, 8, 1, 12, 0))
        };

        var group = Assert.Single(TypeReportRules.GroupByDayDescending(
            rows, r => r.Day, r => r.OccurredAt, r => r.Type, TypeReportSort.NewestFirst));

        Assert.Equal([1, 3, 2], group.Select(r => r.Id));
    }

    [Fact]
    public void GroupByDayDescending_NewestFirst_ReversesTheTieBreakToo()
    {
        // Newest-first is the one ordering rule read backwards, not a second nearly-equal one —
        // so two entries sharing an instant come out in the opposite order as well.
        var rows = new[]
        {
            new Row(1, new DateOnly(2026, 8, 1), At(2026, 8, 1, 9, 0), "Meal"),
            new Row(2, new DateOnly(2026, 8, 1), At(2026, 8, 1, 9, 0), "Cough"),
            new Row(3, new DateOnly(2026, 8, 1), At(2026, 8, 1, 9, 0), "Bleeding")
        };

        var oldestFirst = Assert.Single(
            TypeReportRules.GroupByDayDescending(rows, r => r.Day, r => r.OccurredAt, r => r.Type));
        var newestFirst = Assert.Single(TypeReportRules.GroupByDayDescending(
            rows, r => r.Day, r => r.OccurredAt, r => r.Type, TypeReportSort.NewestFirst));

        Assert.Equal([3, 2, 1], oldestFirst.Select(r => r.Id));
        Assert.Equal([1, 2, 3], newestFirst.Select(r => r.Id));
    }

    [Fact]
    public void GroupByDayDescending_NewestFirst_ReversesEveryDayIncludingItsTies()
    {
        // Several days at once, one of them holding a tie: each day comes out as its own exact
        // reverse — the reversal is within the day, since the days themselves never move.
        var rows = new[]
        {
            new Row(1, new DateOnly(2026, 8, 5), At(2026, 8, 5, 20, 0), "Meal"),
            new Row(2, new DateOnly(2026, 8, 5), At(2026, 8, 5, 7, 0), "Cough"),
            new Row(3, new DateOnly(2026, 8, 5), At(2026, 8, 5, 7, 0), "Bleeding"),
            new Row(4, new DateOnly(2026, 8, 1), At(2026, 8, 1, 8, 0), "Note"),
            new Row(5, new DateOnly(2026, 8, 1), At(2026, 8, 1, 22, 0), "Meal")
        };

        var oldestFirst = TypeReportRules.GroupByDayDescending(rows, r => r.Day, r => r.OccurredAt, r => r.Type);
        var newestFirst = TypeReportRules.GroupByDayDescending(
            rows, r => r.Day, r => r.OccurredAt, r => r.Type, TypeReportSort.NewestFirst);

        Assert.Equal(
            oldestFirst.Select(g => g.Select(r => r.Id).Reverse().ToList()),
            newestFirst.Select(g => g.Select(r => r.Id).ToList()));
        Assert.Equal([[1, 2, 3], [5, 4]], newestFirst.Select(g => g.Select(r => r.Id).ToList()));
    }

    [Fact]
    public void GroupByDayDescending_DaysStayNewestFirstUnderBothSorts()
    {
        // Only the rows inside a day flip — which days a page holds must not depend on the sort.
        var rows = new[]
        {
            new Row(1, new DateOnly(2026, 8, 1), At(2026, 8, 1, 9, 0)),
            new Row(2, new DateOnly(2026, 8, 10), At(2026, 8, 10, 9, 0)),
            new Row(3, new DateOnly(2026, 8, 5), At(2026, 8, 5, 9, 0))
        };

        DateOnly[] expected = [new(2026, 8, 10), new(2026, 8, 5), new(2026, 8, 1)];

        Assert.Equal(expected, TypeReportRules
            .GroupByDayDescending(rows, r => r.Day, r => r.OccurredAt, r => r.Type, TypeReportSort.OldestFirst)
            .Select(g => g.Key));
        Assert.Equal(expected, TypeReportRules
            .GroupByDayDescending(rows, r => r.Day, r => r.OccurredAt, r => r.Type, TypeReportSort.NewestFirst)
            .Select(g => g.Key));
    }

    [Fact]
    public void GroupByDayDescending_SortDefaultsToOldestFirst()
    {
        // The default is what search's call site relies on — it has no toggle and never passes one.
        var rows = new[]
        {
            new Row(1, new DateOnly(2026, 8, 1), At(2026, 8, 1, 18, 0)),
            new Row(2, new DateOnly(2026, 8, 1), At(2026, 8, 1, 9, 0))
        };

        var group = Assert.Single(TypeReportRules.GroupByDayDescending(rows, r => r.Day, r => r.OccurredAt, r => r.Type));

        Assert.Equal([2, 1], group.Select(r => r.Id));
    }

    [Fact]
    public void GroupByDayDescending_NoEntries_IsEmpty()
    {
        Assert.Empty(TypeReportRules.GroupByDayDescending(Array.Empty<Row>(), r => r.Day, r => r.OccurredAt, r => r.Type));
    }

    [Fact]
    public void GroupByDayDescending_OneDayOneEntry_IsOneGroupOfOne()
    {
        var rows = new[] { new Row(1, new DateOnly(2026, 8, 1), At(2026, 8, 1, 9, 0)) };

        var group = Assert.Single(TypeReportRules.GroupByDayDescending(rows, r => r.Day, r => r.OccurredAt, r => r.Type));

        Assert.Equal(new DateOnly(2026, 8, 1), group.Key);
        Assert.Equal([1], group.Select(r => r.Id));
    }
}
