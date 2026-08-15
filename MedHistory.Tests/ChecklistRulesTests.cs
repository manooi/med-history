using MedHistory.Models;
using MedHistory.Services;

namespace MedHistory.Tests;

public class ChecklistRulesTests
{
    private static readonly DateOnly Day = new(2026, 8, 14);

    private static readonly TimeSpan Bangkok = TimeSpan.FromHours(7);

    private static MedAllocation Allocation(
        int id,
        string name,
        MedSlots slots = MedSlots.Morning,
        MealRelation mealRelation = MealRelation.None,
        MedMethod method = MedMethod.Eat) =>
        new() { Id = id, Day = Day, Name = name, Slots = slots, MealRelation = mealRelation, Method = method };

    /// <summary>A tick as it comes off the day's entries: entry id, allocation, slot name.</summary>
    private static ChecklistTick Tick(int entryId, int? allocationId, string? slot) =>
        new(entryId, allocationId, slot);

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
        // Two blank names are not "the same medication".
        Assert.False(ChecklistRules.NamesMatch(a, b));
    }

    // ---- ValidateNewAllocation ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateNewAllocation_EmptyName_ReturnsRequiredError(string? raw)
    {
        var errors = ChecklistRules.ValidateNewAllocation(raw, MedSlots.Morning, []);

        Assert.Contains(errors, e => e.Contains("required"));
    }

    [Fact]
    public void ValidateNewAllocation_UnusedName_NoErrors()
    {
        Assert.Empty(ChecklistRules.ValidateNewAllocation("Eyedrop L", MedSlots.Morning, ["Pill A"]));
    }

    [Theory]
    [InlineData("Pill A")]
    [InlineData("pill a")]
    [InlineData("  PILL A  ")]
    public void ValidateNewAllocation_NameAlreadyOnDay_ReturnsError(string raw)
    {
        // Casing and padding must not smuggle a second row for the same medication in.
        var errors = ChecklistRules.ValidateNewAllocation(raw, MedSlots.Morning, ["Pill A"]);

        Assert.Contains(errors, e => e.Contains("already on this day"));
    }

    [Fact]
    public void ValidateNewAllocation_SameNameOnAnotherDay_NoErrors()
    {
        // The caller only ever passes the target day's names; nothing here is cross-day.
        Assert.Empty(ChecklistRules.ValidateNewAllocation("Pill A", MedSlots.Morning, []));
    }

    [Fact]
    public void ValidateNewAllocation_LongerThanMaxLength_ReturnsError()
    {
        var tooLong = new string('x', ChecklistRules.NameMaxLength + 1);

        Assert.Contains(
            ChecklistRules.ValidateNewAllocation(tooLong, MedSlots.Morning, []),
            e => e.Contains("characters or fewer"));
    }

    [Fact]
    public void ValidateNewAllocation_ExactlyMaxLength_NoErrors()
    {
        Assert.Empty(ChecklistRules.ValidateNewAllocation(
            new string('x', ChecklistRules.NameMaxLength), MedSlots.Morning, []));
    }

    [Fact]
    public void ValidateNewAllocation_NoSlots_ReturnsError()
    {
        // Slots are the doses: a row with none could never be worked through.
        Assert.Contains(
            ChecklistRules.ValidateNewAllocation("Pill A", MedSlots.None, []),
            e => e.Contains("at least one time of day"));
    }

    [Theory]
    [InlineData(MedSlots.Morning)]
    [InlineData(MedSlots.Bedtime)]
    [InlineData(MedSlots.Morning | MedSlots.Evening)]
    [InlineData(MedSlots.Morning | MedSlots.Noon | MedSlots.Evening | MedSlots.Bedtime)]
    public void ValidateNewAllocation_AnyNonEmptySlotSet_NoErrors(MedSlots slots)
    {
        Assert.Empty(ChecklistRules.ValidateNewAllocation("Pill A", slots, []));
    }

    [Fact]
    public void ValidateNewAllocation_SeveralBrokenRules_ReturnsAllOfThem()
    {
        var errors = ChecklistRules.ValidateNewAllocation(
            new string('x', ChecklistRules.NameMaxLength + 1), MedSlots.None, []);

        Assert.Equal(2, errors.Count);
    }

    // ---- DeriveRows ----

    [Fact]
    public void DeriveRows_OneStatePerSlot_InDayOrder()
    {
        var rows = ChecklistRules.DeriveRows([Allocation(1, "Pill A", MedSlots.Bedtime | MedSlots.Morning)], []);

        Assert.Equal([MedSlots.Morning, MedSlots.Bedtime], rows.Single().Slots.Select(s => s.Slot));
        Assert.Equal(["morning", "bedtime"], rows.Single().Slots.Select(s => s.Label));
        Assert.Equal(["Morning", "Bedtime"], rows.Single().Slots.Select(s => s.Name));
    }

    [Fact]
    public void DeriveRows_NothingTicked_EverySlotIsOpen()
    {
        var rows = ChecklistRules.DeriveRows([Allocation(1, "Pill A", MedSlots.Morning | MedSlots.Evening)], []);

        Assert.All(rows.Single().Slots, slot => Assert.False(slot.IsTicked));
        Assert.Equal(0, rows.Single().DoneCount);
        Assert.Equal(2, rows.Single().RequiredCount);
    }

    [Fact]
    public void DeriveRows_ATickMarksOnlyItsOwnSlot()
    {
        var rows = ChecklistRules.DeriveRows(
            [Allocation(1, "Pill A", MedSlots.Morning | MedSlots.Evening)],
            [Tick(10, 1, "Morning")]);

        Assert.Equal([true, false], rows.Single().Slots.Select(s => s.IsTicked));
        Assert.Equal(1, rows.Single().DoneCount);
    }

    [Fact]
    public void DeriveRows_MatchesTheSlotNameIgnoringCase()
    {
        var rows = ChecklistRules.DeriveRows(
            [Allocation(1, "Pill A", MedSlots.Evening)],
            [Tick(10, 1, "evening")]);

        Assert.True(rows.Single().Slots.Single().IsTicked);
    }

    [Fact]
    public void DeriveRows_IgnoresTicksOfOtherAllocations()
    {
        // The same medication allocated twice would still be two independent plans; the link
        // is to the allocation, not to a name.
        var rows = ChecklistRules.DeriveRows(
            [Allocation(1, "Pill A", MedSlots.Morning)],
            [Tick(10, 2, "Morning")]);

        Assert.False(rows.Single().Slots.Single().IsTicked);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Teatime")]
    [InlineData("Morning,Evening")]
    public void DeriveRows_TickWithAnUnusableSlot_MarksNothing(string? slot)
    {
        var rows = ChecklistRules.DeriveRows([Allocation(1, "Pill A", MedSlots.Morning)], [Tick(10, 1, slot)]);

        Assert.False(rows.Single().Slots.Single().IsTicked);
    }

    [Fact]
    public void DeriveRows_TickOfADeletedAllocation_IsIgnored()
    {
        // The dangling case: the allocation is gone, its entry is not. The row that remains
        // must be unaffected and nothing may throw.
        var rows = ChecklistRules.DeriveRows(
            [Allocation(1, "Pill A", MedSlots.Morning)],
            [Tick(10, 99, "Morning"), Tick(11, 1, "Morning")]);

        Assert.Single(rows);
        Assert.True(rows.Single().Slots.Single().IsTicked);
    }

    [Fact]
    public void DeriveRows_OnlyDanglingTicks_LeavesEverythingOpen()
    {
        var rows = ChecklistRules.DeriveRows(
            [Allocation(1, "Pill A", MedSlots.Morning)],
            [Tick(10, 99, "Morning"), Tick(11, null, "Morning")]);

        Assert.False(rows.Single().Slots.Single().IsTicked);
    }

    [Fact]
    public void DeriveRows_KeepsAllocationOrderAndCarriesNameAndDescription()
    {
        var rows = ChecklistRules.DeriveRows(
            [
                Allocation(7, "Pill A", MedSlots.Morning, MealRelation.AfterMeal, MedMethod.Eat),
                Allocation(2, "Eyedrop L", MedSlots.Bedtime, MealRelation.None, MedMethod.Eyedrop)
            ],
            []);

        Assert.Equal([7, 2], rows.Select(r => r.AllocationId));
        Assert.Equal(["Pill A", "Eyedrop L"], rows.Select(r => r.Name));
        Assert.Equal(["after meal · eat", "eyedrop"], rows.Select(r => r.Description));
    }

    [Fact]
    public void DeriveRows_TwoAllocationsTickIndependently()
    {
        var rows = ChecklistRules.DeriveRows(
            [
                Allocation(1, "Pill A", MedSlots.Morning | MedSlots.Evening),
                Allocation(2, "Pill B", MedSlots.Morning)
            ],
            [Tick(10, 1, "Evening"), Tick(11, 2, "Morning")]);

        Assert.Equal([false, true], rows[0].Slots.Select(s => s.IsTicked));
        Assert.Equal([true], rows[1].Slots.Select(s => s.IsTicked));
    }

    [Fact]
    public void DeriveRows_UntouchedAllocation_StillGetsARow()
    {
        Assert.Single(ChecklistRules.DeriveRows([Allocation(1, "Pill A")], []));
    }

    [Fact]
    public void DeriveRows_NoAllocations_ReturnsEmpty()
    {
        Assert.Empty(ChecklistRules.DeriveRows([], [Tick(10, 1, "Morning")]));
    }

    // ---- ChecklistRow display state ----

    [Fact]
    public void ChecklistRow_IsComplete_OnlyWhenEverySlotIsTicked()
    {
        var rows = ChecklistRules.DeriveRows(
            [Allocation(1, "Pill A", MedSlots.Morning | MedSlots.Bedtime)],
            [Tick(10, 1, "Morning")]);

        Assert.False(rows.Single().IsComplete);
    }

    [Fact]
    public void ChecklistRow_IsComplete_WhenTheLastSlotIsTicked()
    {
        var rows = ChecklistRules.DeriveRows(
            [Allocation(1, "Pill A", MedSlots.Morning | MedSlots.Bedtime)],
            [Tick(10, 1, "Morning"), Tick(11, 1, "Bedtime")]);

        Assert.True(rows.Single().IsComplete);
        Assert.Equal(2, rows.Single().DoneCount);
    }

    [Fact]
    public void ChecklistRow_NoSlots_IsNeverComplete()
    {
        // Validation stops such a row being created; if one exists, it must not read as done.
        var rows = ChecklistRules.DeriveRows([Allocation(1, "Pill A", MedSlots.None)], []);

        Assert.Empty(rows.Single().Slots);
        Assert.False(rows.Single().IsComplete);
        Assert.Equal(0, rows.Single().RequiredCount);
    }

    // ---- FindTick ----

    [Fact]
    public void FindTick_ReturnsTheEntryThatTickedTheSlot()
    {
        var tick = ChecklistRules.FindTick(
            [Tick(10, 1, "Morning"), Tick(11, 1, "Evening")],
            1,
            MedSlots.Evening);

        Assert.Equal(11, tick!.Value.EntryId);
    }

    [Fact]
    public void FindTick_SlotNotTicked_ReturnsNull()
    {
        Assert.Null(ChecklistRules.FindTick([Tick(10, 1, "Morning")], 1, MedSlots.Evening));
    }

    [Fact]
    public void FindTick_AnotherAllocationsTick_ReturnsNull()
    {
        Assert.Null(ChecklistRules.FindTick([Tick(10, 2, "Morning")], 1, MedSlots.Morning));
    }

    [Fact]
    public void FindTick_MatchesTheSlotNameIgnoringCase()
    {
        Assert.NotNull(ChecklistRules.FindTick([Tick(10, 1, "  morning ")], 1, MedSlots.Morning));
    }

    [Fact]
    public void FindTick_SeveralEntriesForOneSlot_PicksTheHighestId()
    {
        // Ticking is a no-op on an already-ticked slot, so this should not arise; if it does,
        // the row inserted last is the one an untick undoes.
        var tick = ChecklistRules.FindTick(
            [Tick(10, 1, "Morning"), Tick(12, 1, "Morning"), Tick(11, 1, "Morning")],
            1,
            MedSlots.Morning);

        Assert.Equal(12, tick!.Value.EntryId);
    }

    [Fact]
    public void FindTick_NoSlotAsked_ReturnsNull()
    {
        // What an unparseable slot in the URL degrades to — it must match nothing at all.
        Assert.Null(ChecklistRules.FindTick([Tick(10, 1, "Morning")], 1, MedSlots.None));
    }

    [Fact]
    public void FindTick_UnlinkedTicks_NeverMatch()
    {
        Assert.Null(ChecklistRules.FindTick([Tick(10, null, "Morning")], 1, MedSlots.Morning));
    }

    [Fact]
    public void FindTick_NothingLoggedAtAll_ReturnsNull()
    {
        Assert.Null(ChecklistRules.FindTick([], 1, MedSlots.Morning));
    }

    // ---- AllocationsToCopy ----

    [Fact]
    public void AllocationsToCopy_EmptyTargetDay_CopiesEverything()
    {
        var copied = ChecklistRules.AllocationsToCopy(
            [Allocation(1, "Pill A"), Allocation(2, "Eyedrop L")], []);

        Assert.Equal(["Pill A", "Eyedrop L"], copied.Select(a => a.Name));
    }

    [Fact]
    public void AllocationsToCopy_CarriesTheWholePlan()
    {
        // Structure fidelity: yesterday's schedule is what makes copying forward worth having.
        var source = Allocation(1, "Eyedrop L", MedSlots.Morning | MedSlots.Bedtime, MealRelation.AfterMeal, MedMethod.Eyedrop);

        var copy = ChecklistRules.AllocationsToCopy([source], []).Single();

        Assert.Equal(MedSlots.Morning | MedSlots.Bedtime, copy.Slots);
        Assert.Equal(MealRelation.AfterMeal, copy.MealRelation);
        Assert.Equal(MedMethod.Eyedrop, copy.Method);
        Assert.Equal("Eyedrop L", copy.Name);
    }

    [Theory]
    [InlineData("Pill A")]
    [InlineData("pill a")]
    [InlineData("  PILL A  ")]
    public void AllocationsToCopy_SkipsNamesAlreadyOnTheDay_IgnoringCase(string existing)
    {
        var copied = ChecklistRules.AllocationsToCopy(
            [Allocation(1, "Pill A"), Allocation(2, "Eyedrop L")],
            [existing]);

        Assert.Equal(["Eyedrop L"], copied.Select(a => a.Name));
    }

    [Fact]
    public void AllocationsToCopy_EverythingAlreadyPresent_CopiesNothing()
    {
        var copied = ChecklistRules.AllocationsToCopy(
            [Allocation(1, "Pill A"), Allocation(2, "Eyedrop L")],
            ["Eyedrop L", "pill a"]);

        Assert.Empty(copied);
    }

    [Fact]
    public void AllocationsToCopy_RepeatedSourceName_CopiedOnce()
    {
        var copied = ChecklistRules.AllocationsToCopy(
            [Allocation(1, "Pill A", MedSlots.Morning), Allocation(2, "pill a", MedSlots.Evening)], []);

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
}
