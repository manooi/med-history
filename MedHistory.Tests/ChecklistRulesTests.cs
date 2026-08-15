using MedHistory.Models;
using MedHistory.Services;

namespace MedHistory.Tests;

public class ChecklistRulesTests
{
    private static readonly DateOnly Day = new(2026, 8, 14);

    private static readonly TimeSpan Bangkok = TimeSpan.FromHours(7);

    private static MedAllocation Allocation(int id, string name, int requiredCount = 1) =>
        new() { Id = id, Day = Day, Name = name, RequiredCount = requiredCount };

    private static PillLog Pill(int entryId, string? pillName, int hourUtc) =>
        new(entryId, pillName, new DateTimeOffset(2026, 8, 14, hourUtc, 0, 0, TimeSpan.Zero));

    // ---- NormalizeName ----

    [Fact]
    public void NormalizeName_TrimsSurroundingWhitespace()
    {
        Assert.Equal("Eyedrop L", ChecklistRules.NormalizeName("  Eyedrop L  "));
    }

    [Fact]
    public void NormalizeName_KeepsInnerSpacesAndCasing()
    {
        Assert.Equal("Eyedrop L", ChecklistRules.NormalizeName("Eyedrop L"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n ")]
    public void NormalizeName_NothingLeftAfterTrimming_ReturnsNull(string? raw)
    {
        Assert.Null(ChecklistRules.NormalizeName(raw));
    }

    // ---- NamesMatch ----

    [Theory]
    [InlineData("Pill A", "pill a")]
    [InlineData("pill a", "PILL A")]
    [InlineData("Eyedrop L", "eyedrop l")]
    public void NamesMatch_IgnoresCase(string a, string b)
    {
        Assert.True(ChecklistRules.NamesMatch(a, b));
    }

    [Fact]
    public void NamesMatch_IgnoresSurroundingWhitespace()
    {
        // A hand-typed Pill entry with a stray trailing space still counts towards its allocation.
        Assert.True(ChecklistRules.NamesMatch("Pill A ", " pill a"));
    }

    [Fact]
    public void NamesMatch_DifferentNames_False()
    {
        Assert.False(ChecklistRules.NamesMatch("Pill A", "Pill B"));
    }

    [Theory]
    [InlineData(null, "Pill A")]
    [InlineData("Pill A", null)]
    [InlineData(null, null)]
    [InlineData("", "Pill A")]
    [InlineData("   ", "Pill A")]
    public void NamesMatch_MissingName_False(string? a, string? b)
    {
        // Two blank names are not "the same medication" — a Pill entry with no name
        // must never count towards an allocation.
        Assert.False(ChecklistRules.NamesMatch(a, b));
    }

    // ---- IsPillEntry ----

    [Fact]
    public void IsPillEntry_BuiltInPill_True()
    {
        Assert.True(ChecklistRules.IsPillEntry(BuiltInEntryTypes.Pill));
    }

    [Theory]
    [InlineData(BuiltInEntryTypes.Symptom)]
    [InlineData(BuiltInEntryTypes.Bleeding)]
    [InlineData(BuiltInEntryTypes.Cough)]
    [InlineData(BuiltInEntryTypes.Meal)]
    [InlineData("Blood pressure")]
    public void IsPillEntry_AnyOtherType_False(string type)
    {
        Assert.False(ChecklistRules.IsPillEntry(type));
    }

    [Fact]
    public void IsPillEntry_IsTrueForExactlyOneBuiltIn()
    {
        // A tick creates a built-in Pill entry, so nothing else may ever count towards a
        // checklist row — this pins the check as types are added to the app.
        Assert.Equal([BuiltInEntryTypes.Pill], BuiltInEntryTypes.All.Where(ChecklistRules.IsPillEntry));
    }

    [Theory]
    [InlineData("pill")]
    [InlineData("PILL")]
    public void IsPillEntry_DifferentCase_False(string type)
    {
        // Ordinal on purpose: the database-side filter in DayController.PillLogs is a
        // case-sensitive SQL comparison and the two must agree on every input. Types are
        // stored in their canonical casing, so a variant means a different type.
        Assert.False(ChecklistRules.IsPillEntry(type));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsPillEntry_MissingType_False(string? type)
    {
        Assert.False(ChecklistRules.IsPillEntry(type));
    }

    // ---- ValidateNewAllocation ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateNewAllocation_EmptyName_ReturnsRequiredError(string? raw)
    {
        var errors = ChecklistRules.ValidateNewAllocation(raw, 1, []);

        Assert.Contains(errors, e => e.Contains("required"));
    }

    [Fact]
    public void ValidateNewAllocation_UnusedName_NoErrors()
    {
        Assert.Empty(ChecklistRules.ValidateNewAllocation("Eyedrop L", 2, ["Pill A"]));
    }

    [Theory]
    [InlineData("Pill A")]
    [InlineData("pill a")]
    [InlineData("  PILL A  ")]
    public void ValidateNewAllocation_NameAlreadyOnDay_ReturnsError(string raw)
    {
        // Casing and padding must not smuggle a second row for the same medication in —
        // two rows for one name would both count the same entries.
        var errors = ChecklistRules.ValidateNewAllocation(raw, 1, ["Pill A"]);

        Assert.Contains(errors, e => e.Contains("already on this day"));
    }

    [Fact]
    public void ValidateNewAllocation_SameNameOnAnotherDay_NoErrors()
    {
        // The caller only ever passes the target day's names; nothing here is cross-day.
        Assert.Empty(ChecklistRules.ValidateNewAllocation("Pill A", 1, []));
    }

    [Fact]
    public void ValidateNewAllocation_LongerThanMaxLength_ReturnsError()
    {
        var tooLong = new string('x', ChecklistRules.NameMaxLength + 1);

        Assert.Contains(ChecklistRules.ValidateNewAllocation(tooLong, 1, []), e => e.Contains("characters or fewer"));
    }

    [Fact]
    public void ValidateNewAllocation_ExactlyMaxLength_NoErrors()
    {
        Assert.Empty(ChecklistRules.ValidateNewAllocation(new string('x', ChecklistRules.NameMaxLength), 1, []));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ValidateNewAllocation_CountBelowMinimum_ReturnsError(int requiredCount)
    {
        Assert.Contains(ChecklistRules.ValidateNewAllocation("Pill A", requiredCount, []), e => e.Contains("at least"));
    }

    [Fact]
    public void ValidateNewAllocation_CountAboveMaximum_ReturnsError()
    {
        var errors = ChecklistRules.ValidateNewAllocation("Pill A", ChecklistRules.MaxRequiredCount + 1, []);

        Assert.Contains(errors, e => e.Contains("or fewer"));
    }

    [Theory]
    [InlineData(ChecklistRules.MinRequiredCount)]
    [InlineData(ChecklistRules.MaxRequiredCount)]
    public void ValidateNewAllocation_CountAtBounds_NoErrors(int requiredCount)
    {
        Assert.Empty(ChecklistRules.ValidateNewAllocation("Pill A", requiredCount, []));
    }

    [Fact]
    public void ValidateNewAllocation_SeveralBrokenRules_ReturnsAllOfThem()
    {
        var errors = ChecklistRules.ValidateNewAllocation(new string('x', ChecklistRules.NameMaxLength + 1), 0, []);

        Assert.Equal(2, errors.Count);
    }

    // ---- DeriveProgress ----

    [Fact]
    public void DeriveProgress_CountsMatchingPillEntries()
    {
        var progress = ChecklistRules.DeriveProgress(
            [Allocation(1, "Pill A", 3)],
            [Pill(10, "Pill A", 1), Pill(11, "Pill A", 5)]);

        Assert.Equal(2, progress.Single().DoneCount);
    }

    [Fact]
    public void DeriveProgress_MatchesPillNameIgnoringCase()
    {
        // A Pill entry typed by hand as "pill a" is the same medication.
        var progress = ChecklistRules.DeriveProgress(
            [Allocation(1, "Pill A", 3)],
            [Pill(10, "pill a", 1), Pill(11, "PILL A", 5)]);

        Assert.Equal(2, progress.Single().DoneCount);
    }

    [Fact]
    public void DeriveProgress_IgnoresOtherMedications()
    {
        var progress = ChecklistRules.DeriveProgress(
            [Allocation(1, "Pill A", 2)],
            [Pill(10, "Pill B", 1), Pill(11, "Eyedrop L", 2)]);

        Assert.Equal(0, progress.Single().DoneCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DeriveProgress_IgnoresPillEntriesWithNoName(string? pillName)
    {
        var progress = ChecklistRules.DeriveProgress([Allocation(1, "Pill A", 1)], [Pill(10, pillName, 1)]);

        Assert.Equal(0, progress.Single().DoneCount);
    }

    [Fact]
    public void DeriveProgress_UntouchedAllocation_StillGetsARow()
    {
        var progress = ChecklistRules.DeriveProgress([Allocation(1, "Pill A", 2)], []);

        Assert.Equal(0, progress.Single().DoneCount);
    }

    [Fact]
    public void DeriveProgress_KeepsAllocationOrderAndCarriesNameAndRequirement()
    {
        var progress = ChecklistRules.DeriveProgress(
            [Allocation(7, "Pill A", 3), Allocation(2, "Eyedrop L", 1)],
            [Pill(10, "Eyedrop L", 1)]);

        Assert.Equal([7, 2], progress.Select(p => p.AllocationId));
        Assert.Equal(["Pill A", "Eyedrop L"], progress.Select(p => p.Name));
        Assert.Equal([3, 1], progress.Select(p => p.RequiredCount));
        Assert.Equal([0, 1], progress.Select(p => p.DoneCount));
    }

    [Fact]
    public void DeriveProgress_TwoAllocationsCountIndependently()
    {
        var progress = ChecklistRules.DeriveProgress(
            [Allocation(1, "Pill A", 2), Allocation(2, "Pill B", 2)],
            [Pill(10, "Pill A", 1), Pill(11, "Pill B", 2), Pill(12, "Pill A", 3)]);

        Assert.Equal([2, 1], progress.Select(p => p.DoneCount));
    }

    [Fact]
    public void DeriveProgress_NoAllocations_ReturnsEmpty()
    {
        Assert.Empty(ChecklistRules.DeriveProgress([], [Pill(10, "Pill A", 1)]));
    }

    [Fact]
    public void DeriveProgress_CountsAreNotCapped()
    {
        // Extra doses stay visible to the rules; only the displayed count is capped.
        var progress = ChecklistRules.DeriveProgress(
            [Allocation(1, "Pill A", 1)],
            [Pill(10, "Pill A", 1), Pill(11, "Pill A", 2), Pill(12, "Pill A", 3)]);

        Assert.Equal(3, progress.Single().DoneCount);
    }

    // ---- ChecklistProgress display state ----

    [Fact]
    public void DisplayCount_BelowRequirement_ShowsRawCount()
    {
        Assert.Equal(1, new ChecklistProgress(1, "Pill A", 3, 1).DisplayCount);
    }

    [Fact]
    public void DisplayCount_AboveRequirement_CapsAtRequired()
    {
        // Four doses of a three-dose medication still reads 3/3 — the extra entries survive.
        Assert.Equal(3, new ChecklistProgress(1, "Pill A", 3, 4).DisplayCount);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(9, true)]
    public void IsComplete_TrueOnceRequirementIsMet(int doneCount, bool expected)
    {
        Assert.Equal(expected, new ChecklistProgress(1, "Pill A", 3, doneCount).IsComplete);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void CanUntick_NeedsSomethingLogged(int doneCount, bool expected)
    {
        Assert.Equal(expected, new ChecklistProgress(1, "Pill A", 3, doneCount).CanUntick);
    }

    // ---- AllocationsToCopy ----

    [Fact]
    public void AllocationsToCopy_EmptyTargetDay_CopiesEverything()
    {
        var copied = ChecklistRules.AllocationsToCopy([Allocation(1, "Pill A", 3), Allocation(2, "Eyedrop L")], []);

        Assert.Equal(["Pill A", "Eyedrop L"], copied.Select(a => a.Name));
    }

    [Fact]
    public void AllocationsToCopy_CarriesTheRequiredCount()
    {
        var copied = ChecklistRules.AllocationsToCopy([Allocation(1, "Pill A", 3)], []);

        Assert.Equal(3, copied.Single().RequiredCount);
    }

    [Theory]
    [InlineData("Pill A")]
    [InlineData("pill a")]
    [InlineData("  PILL A  ")]
    public void AllocationsToCopy_SkipsNamesAlreadyOnTheDay_IgnoringCase(string existing)
    {
        var copied = ChecklistRules.AllocationsToCopy(
            [Allocation(1, "Pill A", 3), Allocation(2, "Eyedrop L")],
            [existing]);

        Assert.Equal(["Eyedrop L"], copied.Select(a => a.Name));
    }

    [Fact]
    public void AllocationsToCopy_EverythingAlreadyPresent_CopiesNothing()
    {
        var copied = ChecklistRules.AllocationsToCopy(
            [Allocation(1, "Pill A", 3), Allocation(2, "Eyedrop L")],
            ["Eyedrop L", "pill a"]);

        Assert.Empty(copied);
    }

    [Fact]
    public void AllocationsToCopy_RepeatedSourceName_CopiedOnce()
    {
        var copied = ChecklistRules.AllocationsToCopy([Allocation(1, "Pill A", 3), Allocation(2, "pill a", 1)], []);

        Assert.Equal(["Pill A"], copied.Select(a => a.Name));
    }

    [Fact]
    public void AllocationsToCopy_EmptySource_ReturnsEmpty()
    {
        Assert.Empty(ChecklistRules.AllocationsToCopy([], ["Pill A"]));
    }

    // ---- TickTime ----

    [Fact]
    public void TickTime_Today_IsTheCurrentInstant()
    {
        var now = new DateTimeOffset(2026, 8, 14, 3, 24, 0, TimeSpan.Zero);

        Assert.Equal(now, ChecklistRules.TickTime(Day, Day, now, Bangkok));
    }

    [Fact]
    public void TickTime_PastDay_IsNoonLocalOnThatDay()
    {
        var tick = ChecklistRules.TickTime(Day, Day.AddDays(1), DateTimeOffset.UtcNow, Bangkok);

        // 12:00 at UTC+07:00 is 05:00 UTC.
        Assert.Equal(new DateTimeOffset(2026, 8, 14, 5, 0, 0, TimeSpan.Zero), tick);
    }

    [Fact]
    public void TickTime_FutureDay_IsAlsoNoonLocal()
    {
        var tick = ChecklistRules.TickTime(Day, Day.AddDays(-3), DateTimeOffset.UtcNow, Bangkok);

        Assert.Equal(new DateTimeOffset(2026, 8, 14, 5, 0, 0, TimeSpan.Zero), tick);
    }

    [Fact]
    public void TickTime_PastDay_StaysInsideThatLocalDay_ForAWesternOffset()
    {
        var offset = TimeSpan.FromHours(-8);

        var tick = ChecklistRules.TickTime(Day, Day.AddDays(1), DateTimeOffset.UtcNow, offset);

        // Noon is far enough from either midnight that the day survives the round trip.
        Assert.Equal(Day, DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(tick, TimeZoneInfo.CreateCustomTimeZone("t", offset, "t", "t")).DateTime));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(-8)]
    [InlineData(0)]
    public void TickTime_AlwaysReturnsUtc(int offsetHours)
    {
        // Npgsql rejects a non-zero offset on a timestamptz column.
        var tick = ChecklistRules.TickTime(Day, Day.AddDays(1), DateTimeOffset.UtcNow, TimeSpan.FromHours(offsetHours));

        Assert.Equal(TimeSpan.Zero, tick.Offset);
    }

    [Fact]
    public void TickTime_Today_ReturnsUtcEvenWhenNowCarriesAnOffset()
    {
        var now = new DateTimeOffset(2026, 8, 14, 10, 24, 0, Bangkok);

        var tick = ChecklistRules.TickTime(Day, Day, now, Bangkok);

        Assert.Equal(TimeSpan.Zero, tick.Offset);
        Assert.Equal(now, tick);
    }

    // ---- NewestMatch ----

    [Fact]
    public void NewestMatch_PicksTheLatestEntryOfThatMedication()
    {
        var newest = ChecklistRules.NewestMatch(
            [Pill(10, "Pill A", 1), Pill(11, "Pill A", 9), Pill(12, "Pill A", 5)],
            "Pill A");

        Assert.Equal(11, newest!.Value.EntryId);
    }

    [Fact]
    public void NewestMatch_IdenticalTimestamps_PicksTheHighestId()
    {
        // Ticking twice within a second is the realistic way to get here; the row inserted
        // last is the one the untick undoes.
        var newest = ChecklistRules.NewestMatch(
            [Pill(10, "Pill A", 5), Pill(12, "Pill A", 5), Pill(11, "Pill A", 5)],
            "Pill A");

        Assert.Equal(12, newest!.Value.EntryId);
    }

    [Fact]
    public void NewestMatch_MatchesIgnoringCase()
    {
        var newest = ChecklistRules.NewestMatch([Pill(10, "pill a", 1)], "Pill A");

        Assert.Equal(10, newest!.Value.EntryId);
    }

    [Fact]
    public void NewestMatch_IgnoresOtherMedications()
    {
        var newest = ChecklistRules.NewestMatch(
            [Pill(10, "Pill A", 1), Pill(11, "Pill B", 9)],
            "Pill A");

        Assert.Equal(10, newest!.Value.EntryId);
    }

    [Fact]
    public void NewestMatch_NothingLogged_ReturnsNull()
    {
        Assert.Null(ChecklistRules.NewestMatch([Pill(10, "Pill B", 1)], "Pill A"));
    }

    [Fact]
    public void NewestMatch_NoEntriesAtAll_ReturnsNull()
    {
        Assert.Null(ChecklistRules.NewestMatch([], "Pill A"));
    }

    [Fact]
    public void NewestMatch_UnnamedPillEntries_NeverMatch()
    {
        Assert.Null(ChecklistRules.NewestMatch([Pill(10, null, 1), Pill(11, "  ", 2)], "Pill A"));
    }
}
