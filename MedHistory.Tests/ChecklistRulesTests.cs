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
        MedMethod method = MedMethod.Eat,
        decimal doseQuantity = 1m) =>
        new()
        {
            Id = id,
            Day = Day,
            Name = name,
            Slots = slots,
            DoseQuantity = doseQuantity,
            MealRelation = mealRelation,
            Method = method
        };

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

    // ---- ValidateRange ----

    [Fact]
    public void ValidateRange_SingleDay_NoErrors()
    {
        Assert.Empty(ChecklistRules.ValidateRange(Day, Day));
    }

    [Fact]
    public void ValidateRange_ToBeforeFrom_ReturnsError()
    {
        Assert.Contains(
            ChecklistRules.ValidateRange(Day, Day.AddDays(-1)),
            e => e.Contains("on or after"));
    }

    [Fact]
    public void ValidateRange_ExactlyMaxDays_NoErrors()
    {
        var to = Day.AddDays(ChecklistRules.MaxRangeDays - 1);

        Assert.Equal(ChecklistRules.MaxRangeDays, ChecklistRules.RangeLength(Day, to));
        Assert.Empty(ChecklistRules.ValidateRange(Day, to));
    }

    [Fact]
    public void ValidateRange_OneDayOverMax_ReturnsError()
    {
        var to = Day.AddDays(ChecklistRules.MaxRangeDays);

        Assert.Equal(ChecklistRules.MaxRangeDays + 1, ChecklistRules.RangeLength(Day, to));
        Assert.Contains(ChecklistRules.ValidateRange(Day, to), e => e.Contains("Range too long"));
    }

    // ---- ExpandRange ----

    [Fact]
    public void ExpandRange_SingleDay_ReturnsThatOneDay()
    {
        Assert.Equal([Day], ChecklistRules.ExpandRange(Day, Day));
    }

    [Fact]
    public void ExpandRange_MultiDay_ReturnsEveryDayInOrder()
    {
        Assert.Equal(
            [Day, Day.AddDays(1), Day.AddDays(2)],
            ChecklistRules.ExpandRange(Day, Day.AddDays(2)));
    }

    [Fact]
    public void ExpandRange_ToBeforeFrom_ReturnsEmpty()
    {
        // Validation rejects this range; expansion just refuses to loop backwards.
        Assert.Empty(ChecklistRules.ExpandRange(Day, Day.AddDays(-1)));
    }

    [Fact]
    public void ExpandRange_SpansALeapDay_IncludesIt()
    {
        var from = new DateOnly(2028, 2, 28);
        var to = new DateOnly(2028, 3, 1);

        Assert.Equal(
            [new DateOnly(2028, 2, 28), new DateOnly(2028, 2, 29), new DateOnly(2028, 3, 1)],
            ChecklistRules.ExpandRange(from, to));
    }

    // ---- DaysToAllocate ----

    [Fact]
    public void DaysToAllocate_NothingExisting_KeepsEveryDay()
    {
        var days = ChecklistRules.ExpandRange(Day, Day.AddDays(2));

        Assert.Equal(days, ChecklistRules.DaysToAllocate(days, "Pill A", new Dictionary<DateOnly, IReadOnlyList<string>>()));
    }

    [Fact]
    public void DaysToAllocate_SomeDaysAlreadyHaveTheName_SkipsOnlyThose()
    {
        var days = ChecklistRules.ExpandRange(Day, Day.AddDays(2));
        var existing = new Dictionary<DateOnly, IReadOnlyList<string>>
        {
            [Day.AddDays(1)] = ["Pill A"]
        };

        Assert.Equal([Day, Day.AddDays(2)], ChecklistRules.DaysToAllocate(days, "Pill A", existing));
    }

    [Fact]
    public void DaysToAllocate_MatchesExistingNameIgnoringCaseAndPadding()
    {
        var days = ChecklistRules.ExpandRange(Day, Day);
        var existing = new Dictionary<DateOnly, IReadOnlyList<string>> { [Day] = ["  pill a  "] };

        Assert.Empty(ChecklistRules.DaysToAllocate(days, "Pill A", existing));
    }

    [Fact]
    public void DaysToAllocate_EveryDayAlreadyHasTheName_ReturnsEmpty_NotAnError()
    {
        var days = ChecklistRules.ExpandRange(Day, Day.AddDays(1));
        var existing = new Dictionary<DateOnly, IReadOnlyList<string>>
        {
            [Day] = ["Pill A"],
            [Day.AddDays(1)] = ["Pill A"]
        };

        Assert.Empty(ChecklistRules.DaysToAllocate(days, "Pill A", existing));
    }

    [Fact]
    public void DaysToAllocate_ExistingNamesOnDay_OtherNamesDoNotBlockIt()
    {
        var days = ChecklistRules.ExpandRange(Day, Day);
        var existing = new Dictionary<DateOnly, IReadOnlyList<string>> { [Day] = ["Eyedrop L"] };

        Assert.Equal([Day], ChecklistRules.DaysToAllocate(days, "Pill A", existing));
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

    // ---- AffectedAllocations ----

    private static ChecklistRules.AllocationRef Ref(int id, DateOnly day, string name) => new(id, day, name);

    [Fact]
    public void AffectedAllocations_NoApplyForward_IsJustTheEditedRow()
    {
        var edited = Ref(1, Day, "Pill A");
        var candidates = new[] { edited, Ref(2, Day.AddDays(1), "Pill A") };

        var affected = ChecklistRules.AffectedAllocations(edited, applyForward: false, candidates);

        Assert.Equal([edited], affected);
    }

    [Fact]
    public void AffectedAllocations_ApplyForward_IncludesFutureRowsWithTheSameName()
    {
        var edited = Ref(1, Day, "Pill A");
        var future = Ref(2, Day.AddDays(1), "Pill A");

        var affected = ChecklistRules.AffectedAllocations(edited, applyForward: true, [edited, future]);

        Assert.Equal([1, 2], affected.Select(a => a.Id));
    }

    [Fact]
    public void AffectedAllocations_ApplyForward_LeavesPastDaysUntouchedEvenWithTheSameName()
    {
        var edited = Ref(1, Day, "Pill A");
        var past = Ref(2, Day.AddDays(-1), "Pill A");

        var affected = ChecklistRules.AffectedAllocations(edited, applyForward: true, [edited, past]);

        Assert.Equal([1], affected.Select(a => a.Id));
    }

    [Fact]
    public void AffectedAllocations_ApplyForward_LeavesOtherNamedRowsUntouched()
    {
        var edited = Ref(1, Day, "Pill A");
        var otherName = Ref(2, Day.AddDays(1), "Pill B");

        var affected = ChecklistRules.AffectedAllocations(edited, applyForward: true, [edited, otherName]);

        Assert.Equal([1], affected.Select(a => a.Id));
    }

    [Fact]
    public void AffectedAllocations_ApplyForward_MatchesTheOldNameIgnoringCase()
    {
        var edited = Ref(1, Day, "Pill A");
        var future = Ref(2, Day.AddDays(1), "pill a");

        var affected = ChecklistRules.AffectedAllocations(edited, applyForward: true, [edited, future]);

        Assert.Equal([1, 2], affected.Select(a => a.Id));
    }

    // ---- RenameCollisionDays ----

    [Fact]
    public void RenameCollisionDays_NoRenameEdit_ReturnsEmpty()
    {
        // Saving with the name unchanged: the only row with that name on the affected day is
        // the row being edited itself, which is excluded.
        var namesByDay = new Dictionary<DateOnly, IReadOnlyList<ChecklistRules.AllocationRef>>
        {
            [Day] = [Ref(1, Day, "Pill A")]
        };

        var collisions = ChecklistRules.RenameCollisionDays("Pill A", new HashSet<int> { 1 }, namesByDay);

        Assert.Empty(collisions);
    }

    [Fact]
    public void RenameCollisionDays_RenameWithNoOtherRowUsingTheName_ReturnsEmpty()
    {
        var namesByDay = new Dictionary<DateOnly, IReadOnlyList<ChecklistRules.AllocationRef>>
        {
            [Day] = [Ref(1, Day, "Pill A")]
        };

        var collisions = ChecklistRules.RenameCollisionDays("Eyedrop L", new HashSet<int> { 1 }, namesByDay);

        Assert.Empty(collisions);
    }

    [Fact]
    public void RenameCollisionDays_RenameCollidesOnTheSameDay_ReturnsThatDay()
    {
        var namesByDay = new Dictionary<DateOnly, IReadOnlyList<ChecklistRules.AllocationRef>>
        {
            [Day] = [Ref(1, Day, "Pill A"), Ref(2, Day, "Eyedrop L")]
        };

        var collisions = ChecklistRules.RenameCollisionDays("Eyedrop L", new HashSet<int> { 1 }, namesByDay);

        Assert.Equal([Day], collisions);
    }

    [Fact]
    public void RenameCollisionDays_MatchesTheNewNameIgnoringCaseAndPadding()
    {
        var namesByDay = new Dictionary<DateOnly, IReadOnlyList<ChecklistRules.AllocationRef>>
        {
            [Day] = [Ref(1, Day, "Pill A"), Ref(2, Day, "  eyedrop l  ")]
        };

        var collisions = ChecklistRules.RenameCollisionDays("Eyedrop L", new HashSet<int> { 1 }, namesByDay);

        Assert.Equal([Day], collisions);
    }

    [Fact]
    public void RenameCollisionDays_WithoutApplyForward_OnlyThisDayIsChecked()
    {
        // The caller passes only this day's names when applyForward is off, so a same-name
        // collision on a later day is never seen.
        var namesByDay = new Dictionary<DateOnly, IReadOnlyList<ChecklistRules.AllocationRef>>
        {
            [Day] = [Ref(1, Day, "Pill A")]
        };

        var collisions = ChecklistRules.RenameCollisionDays("Eyedrop L", new HashSet<int> { 1 }, namesByDay);

        Assert.Empty(collisions);
    }

    [Fact]
    public void RenameCollisionDays_WithApplyForward_ALaterDayCollisionIsDetected()
    {
        // The caller passes every affected day's names when applyForward is on.
        var laterDay = Day.AddDays(1);
        var namesByDay = new Dictionary<DateOnly, IReadOnlyList<ChecklistRules.AllocationRef>>
        {
            [Day] = [Ref(1, Day, "Pill A")],
            [laterDay] = [Ref(2, laterDay, "Pill A"), Ref(3, laterDay, "Eyedrop L")]
        };

        var collisions = ChecklistRules.RenameCollisionDays(
            "Eyedrop L", new HashSet<int> { 1, 2 }, namesByDay);

        Assert.Equal([laterDay], collisions);
    }

    [Fact]
    public void RenameCollisionDays_MultipleCollidingDays_ReturnsAllInOrder()
    {
        var laterDay = Day.AddDays(1);
        var namesByDay = new Dictionary<DateOnly, IReadOnlyList<ChecklistRules.AllocationRef>>
        {
            [laterDay] = [Ref(2, laterDay, "Eyedrop L")],
            [Day] = [Ref(1, Day, "Eyedrop L")]
        };

        var collisions = ChecklistRules.RenameCollisionDays("Eyedrop L", new HashSet<int>(), namesByDay);

        Assert.Equal([Day, laterDay], collisions);
    }

    // ---- JoinDayLabels ----

    [Fact]
    public void JoinDayLabels_AtOrBelowTheCap_ListsEveryLabel()
    {
        Assert.Equal("a, b, c", ChecklistRules.JoinDayLabels(["a", "b", "c"]));
    }

    [Fact]
    public void JoinDayLabels_OverTheCap_SummarisesTheRemainder()
    {
        Assert.Equal("a, b, c, and 2 more", ChecklistRules.JoinDayLabels(["a", "b", "c", "d", "e"]));
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

    // ---- ValidateDoseQuantity ----

    [Theory]
    [InlineData("1", 1)]
    [InlineData("0.25", 0.25)]
    [InlineData("0.5", 0.5)]
    [InlineData("2", 2)]
    [InlineData("2.75", 2.75)]
    [InlineData("99", 99)]
    public void ValidateDoseQuantity_AQuarterUnitStepInRange_IsAccepted(string raw, double expected)
    {
        Assert.Empty(ChecklistRules.ValidateDoseQuantity(raw, out var quantity));
        Assert.Equal((decimal)expected, quantity);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateDoseQuantity_Missing_IsRejected(string? raw)
    {
        Assert.Single(ChecklistRules.ValidateDoseQuantity(raw, out _));
    }

    [Theory]
    [InlineData("two")]
    [InlineData("1/2")]
    [InlineData("½")]
    public void ValidateDoseQuantity_NotANumber_IsRejected(string raw)
    {
        Assert.Single(ChecklistRules.ValidateDoseQuantity(raw, out _));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.1")]
    [InlineData("99.25")]
    [InlineData("1000")]
    public void ValidateDoseQuantity_OutsideTheBounds_IsRejected(string raw)
    {
        // Zero included: a plan to take nothing is not a plan.
        Assert.Single(ChecklistRules.ValidateDoseQuantity(raw, out _));
    }

    [Theory]
    [InlineData("0.3")]
    [InlineData("1.1")]
    [InlineData("2.333")]
    public void ValidateDoseQuantity_OffTheStep_IsRejectedRatherThanRounded(string raw)
    {
        // The column keeps two decimals, so accepting these would store a number never typed.
        Assert.Single(ChecklistRules.ValidateDoseQuantity(raw, out _));
    }

    [Fact]
    public void ValidateDoseQuantity_OutOfRange_ComplainsOnlyOnce()
    {
        // 0.1 is both below the minimum and off the step; naming both reads as two problems.
        Assert.Single(ChecklistRules.ValidateDoseQuantity("0.1", out _));
    }

    [Fact]
    public void ValidateDoseQuantity_Rejected_LeavesTheDefaultQuantity()
    {
        // Nothing may end up stored from a rejected submit, but the out value must still be
        // usable rather than zero — every caller re-renders the form and drops it.
        ChecklistRules.ValidateDoseQuantity("nonsense", out var quantity);

        Assert.Equal(MedPlanRules.DefaultDoseQuantity, quantity);
    }

    [Fact]
    public void ValidateDoseQuantity_AcceptsWhatTheFormOffers()
    {
        // Whatever the form's min/max/step allow must survive the server's own rules.
        for (var value = MedPlanRules.MinDoseQuantity;
             value <= MedPlanRules.MaxDoseQuantity;
             value += MedPlanRules.DoseQuantityStep)
        {
            Assert.Empty(ChecklistRules.ValidateDoseQuantity(MedPlanRules.FormatQuantity(value), out _));
        }
    }

    // ---- DeriveRows: dose quantity ----

    [Fact]
    public void DeriveRows_CarriesTheAllocationsDoseQuantity()
    {
        var rows = ChecklistRules.DeriveRows([Allocation(1, "Pill A", doseQuantity: 2m)], []);

        Assert.Equal(2m, rows[0].DoseQuantity);
        Assert.Equal("×2", rows[0].QuantityLabel);
    }

    [Fact]
    public void DeriveRows_OneUnit_ShowsNoQuantity()
    {
        var rows = ChecklistRules.DeriveRows([Allocation(1, "Pill A")], []);

        Assert.Empty(rows[0].QuantityLabel);
    }

    // ---- DeriveRows: stock ----

    [Fact]
    public void DeriveRows_NoStockPassed_ShowsNoCount()
    {
        var rows = ChecklistRules.DeriveRows([Allocation(1, "Pill A")], []);

        Assert.Null(rows[0].StockRemaining);
        Assert.Empty(rows[0].StockLabel);
    }

    [Fact]
    public void DeriveRows_StockNamingThisMedication_ShowsWhatIsLeft()
    {
        var rows = ChecklistRules.DeriveRows(
            [Allocation(1, "Pill A")],
            [],
            [new MedStockRow(1, "Pill A", 30m, 12m)]);

        Assert.Equal(18m, rows[0].StockRemaining);
        Assert.Equal("(18 left)", rows[0].StockLabel);
    }

    [Fact]
    public void DeriveRows_StockIsMatchedIgnoringCaseAndSpacing()
    {
        var rows = ChecklistRules.DeriveRows(
            [Allocation(1, "Eyedrop L")],
            [],
            [new MedStockRow(1, " eyedrop l ", 10m, 4m)]);

        Assert.Equal(6m, rows[0].StockRemaining);
    }

    [Fact]
    public void DeriveRows_StockNamingSomethingElse_ShowsNoCount()
    {
        var rows = ChecklistRules.DeriveRows(
            [Allocation(1, "Pill A")],
            [],
            [new MedStockRow(1, "Pill B", 30m, 0m)]);

        Assert.Null(rows[0].StockRemaining);
    }

    [Fact]
    public void DeriveRows_OverdrawnStock_StillTicksAndShowsTheShortfall()
    {
        // Running out is information, never a reason the day cannot be worked through.
        var rows = ChecklistRules.DeriveRows(
            [Allocation(1, "Pill A", MedSlots.Morning)],
            [Tick(10, 1, "Morning")],
            [new MedStockRow(1, "Pill A", 5m, 7m)]);

        Assert.Equal(-2m, rows[0].StockRemaining);
        Assert.Equal("(-2 left)", rows[0].StockLabel);
        Assert.True(rows[0].Slots[0].IsTicked);
    }
}
