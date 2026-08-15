using MedHistory.Models;
using MedHistory.Services;

namespace MedHistory.Tests;

public class MedPlanRulesTests
{
    /// <summary>Every subset of the four slots — 16 values, the whole domain of a slot set.</summary>
    private static IEnumerable<MedSlots> EverySlotSet()
    {
        for (var bits = 0; bits < 16; bits++)
        {
            yield return (MedSlots)bits;
        }
    }

    // ---- FormatSlots ----

    [Fact]
    public void FormatSlots_SingleSlot_IsItsName()
    {
        Assert.Equal("Morning", MedPlanRules.FormatSlots(MedSlots.Morning));
    }

    [Fact]
    public void FormatSlots_SeveralSlots_AreCommaSeparatedWithNoSpaces()
    {
        Assert.Equal("Morning,Bedtime", MedPlanRules.FormatSlots(MedSlots.Morning | MedSlots.Bedtime));
    }

    [Fact]
    public void FormatSlots_IsAlwaysInDayOrder()
    {
        // However the set was built, the stored string reads morning-first.
        Assert.Equal(
            "Morning,Noon,Evening,Bedtime",
            MedPlanRules.FormatSlots(MedSlots.Bedtime | MedSlots.Noon | MedSlots.Morning | MedSlots.Evening));
    }

    [Fact]
    public void FormatSlots_NoSlots_IsEmpty()
    {
        Assert.Equal(string.Empty, MedPlanRules.FormatSlots(MedSlots.None));
    }

    [Fact]
    public void FormatSlots_EveryPossibleSetFitsTheColumn()
    {
        // The stored column is capped at SlotsMaxLength; nothing the app can build may exceed it.
        Assert.All(EverySlotSet(), slots =>
            Assert.True(MedPlanRules.FormatSlots(slots).Length <= MedPlanRules.SlotsMaxLength));
    }

    [Fact]
    public void SlotName_FitsTheEntryColumn()
    {
        // An entry stores one slot name; the column is capped at SlotNameMaxLength.
        Assert.All(MedPlanRules.AllSlots, slot =>
            Assert.True(MedPlanRules.SlotName(slot).Length <= MedPlanRules.SlotNameMaxLength));
    }

    // ---- ParseSlots ----

    [Fact]
    public void ParseSlots_EveryPossibleSetSurvivesARoundTrip()
    {
        // The whole point of the stored format: what goes into Postgres comes back unchanged.
        Assert.All(EverySlotSet(), slots =>
            Assert.Equal(slots, MedPlanRules.ParseSlots(MedPlanRules.FormatSlots(slots))));
    }

    [Theory]
    [InlineData("morning,bedtime")]
    [InlineData("MORNING,BEDTIME")]
    [InlineData(" Morning , Bedtime ")]
    public void ParseSlots_IgnoresCasingAndPadding(string raw)
    {
        Assert.Equal(MedSlots.Morning | MedSlots.Bedtime, MedPlanRules.ParseSlots(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",,,")]
    public void ParseSlots_NothingUsable_IsNone(string? raw)
    {
        Assert.Equal(MedSlots.None, MedPlanRules.ParseSlots(raw));
    }

    [Fact]
    public void ParseSlots_UnknownNamesAreDropped_NotThrown()
    {
        // A stored value is read on every page load: a name we no longer understand must cost
        // that one slot, never the day view.
        Assert.Equal(MedSlots.Morning, MedPlanRules.ParseSlots("Morning,Teatime,42"));
    }

    [Fact]
    public void ParseSlots_FromCheckboxNames_BuildsTheSet()
    {
        Assert.Equal(
            MedSlots.Noon | MedSlots.Evening,
            MedPlanRules.ParseSlots(["Noon", "Evening"]));
    }

    [Fact]
    public void ParseSlots_FromCheckboxNames_RepeatsAreHarmless()
    {
        Assert.Equal(MedSlots.Noon, MedPlanRules.ParseSlots(["Noon", "noon", " NOON "]));
    }

    [Fact]
    public void ParseSlots_FromCheckboxNames_EmptyAndUnknownContributeNothing()
    {
        Assert.Equal(MedSlots.None, MedPlanRules.ParseSlots([null, "", "  ", "Teatime"]));
    }

    // ---- Each / SlotCount ----

    [Fact]
    public void Each_ReturnsTheSlotsInDayOrder()
    {
        Assert.Equal(
            [MedSlots.Morning, MedSlots.Evening],
            MedPlanRules.Each(MedSlots.Evening | MedSlots.Morning));
    }

    [Fact]
    public void Each_NoSlots_IsEmpty()
    {
        Assert.Empty(MedPlanRules.Each(MedSlots.None));
    }

    [Fact]
    public void Each_IgnoresBitsOutsideTheKnownSlots()
    {
        // 16 is not a slot; the set degrades to the slots we understand.
        Assert.Equal([MedSlots.Morning], MedPlanRules.Each(MedSlots.Morning | (MedSlots)16));
    }

    [Theory]
    [InlineData(MedSlots.None, 0)]
    [InlineData(MedSlots.Morning, 1)]
    [InlineData(MedSlots.Morning | MedSlots.Bedtime, 2)]
    public void SlotCount_CountsTheSlotsSet(MedSlots slots, int expected)
    {
        Assert.Equal(expected, MedPlanRules.SlotCount(slots));
    }

    [Fact]
    public void SlotCount_AllSlots_IsFour()
    {
        Assert.Equal(4, MedPlanRules.SlotCount(MedSlots.Morning | MedSlots.Noon | MedSlots.Evening | MedSlots.Bedtime));
    }

    // ---- TryParseSlot ----

    [Fact]
    public void TryParseSlot_KnownName_Succeeds()
    {
        Assert.True(MedPlanRules.TryParseSlot("Evening", out var slot));
        Assert.Equal(MedSlots.Evening, slot);
    }

    [Theory]
    [InlineData("evening")]
    [InlineData("EVENING")]
    [InlineData("  Evening  ")]
    public void TryParseSlot_IgnoresCasingAndPadding(string name)
    {
        Assert.True(MedPlanRules.TryParseSlot(name, out var slot));
        Assert.Equal(MedSlots.Evening, slot);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Teatime")]
    [InlineData("None")]
    [InlineData("1")]
    public void TryParseSlot_UnusableName_FailsAndYieldsNone(string? name)
    {
        Assert.False(MedPlanRules.TryParseSlot(name, out var slot));
        Assert.Equal(MedSlots.None, slot);
    }

    [Fact]
    public void TryParseSlot_ASetIsNotASingleSlot()
    {
        // An entry records one slot; a stored pair means the value is corrupt, not a tick.
        Assert.False(MedPlanRules.TryParseSlot("Morning,Noon", out _));
    }

    [Fact]
    public void TryParseSlot_ReadsBackEverySlotNameWeWrite()
    {
        Assert.All(MedPlanRules.AllSlots, slot =>
        {
            Assert.True(MedPlanRules.TryParseSlot(MedPlanRules.SlotName(slot), out var parsed));
            Assert.Equal(slot, parsed);
        });
    }

    // ---- Labels ----

    [Theory]
    [InlineData(MedSlots.Morning, "morning")]
    [InlineData(MedSlots.Noon, "noon")]
    [InlineData(MedSlots.Evening, "evening")]
    [InlineData(MedSlots.Bedtime, "bedtime")]
    public void SlotLabel_NamesTheSlot(MedSlots slot, string expected)
    {
        Assert.Equal(expected, MedPlanRules.SlotLabel(slot));
    }

    [Fact]
    public void SlotLabel_EverySlotHasOne()
    {
        Assert.All(MedPlanRules.AllSlots, slot => Assert.NotEmpty(MedPlanRules.SlotLabel(slot)));
    }

    [Theory]
    [InlineData(MedSlots.None)]
    [InlineData(MedSlots.Morning | MedSlots.Noon)]
    public void SlotLabel_NotASingleSlot_IsEmpty(MedSlots slots)
    {
        Assert.Empty(MedPlanRules.SlotLabel(slots));
    }

    [Theory]
    [InlineData(MealRelation.BeforeMeal, "before meal")]
    [InlineData(MealRelation.AfterMeal, "after meal")]
    [InlineData(MealRelation.WithMeal, "with meal")]
    public void MealRelationLabel_NamesTheRelation(MealRelation relation, string expected)
    {
        Assert.Equal(expected, MedPlanRules.MealRelationLabel(relation));
    }

    [Fact]
    public void MealRelationLabel_None_IsEmpty()
    {
        // "It does not matter" is worth nothing on screen, so the composers drop it.
        Assert.Empty(MedPlanRules.MealRelationLabel(MealRelation.None));
    }

    [Theory]
    [InlineData(MedMethod.Eat, "eat")]
    [InlineData(MedMethod.Apply, "apply")]
    [InlineData(MedMethod.Eyedrop, "eyedrop")]
    [InlineData(MedMethod.Inject, "inject")]
    public void MethodLabel_NamesTheMethod(MedMethod method, string expected)
    {
        Assert.Equal(expected, MedPlanRules.MethodLabel(method));
    }

    [Fact]
    public void MethodLabel_Other_IsEmpty()
    {
        Assert.Empty(MedPlanRules.MethodLabel(MedMethod.Other));
    }

    // ---- Dropdown options ----

    [Fact]
    public void MealRelationOption_None_ReadsAsAnyTime()
    {
        Assert.Equal("any time", MedPlanRules.MealRelationOption(MealRelation.None));
    }

    [Fact]
    public void MethodOption_Other_ReadsAsOther()
    {
        Assert.Equal("other", MedPlanRules.MethodOption(MedMethod.Other));
    }

    [Fact]
    public void EveryMealRelationHasADropdownOption()
    {
        // A member with no option label would render as a blank row the user cannot read.
        Assert.All(Enum.GetValues<MealRelation>(), relation =>
            Assert.NotEmpty(MedPlanRules.MealRelationOption(relation)));
    }

    [Fact]
    public void EveryMethodHasADropdownOption()
    {
        Assert.All(Enum.GetValues<MedMethod>(), method =>
            Assert.NotEmpty(MedPlanRules.MethodOption(method)));
    }

    [Fact]
    public void Options_MatchTheInlineLabelWhereThereIsOne()
    {
        Assert.Equal("after meal", MedPlanRules.MealRelationOption(MealRelation.AfterMeal));
        Assert.Equal("eyedrop", MedPlanRules.MethodOption(MedMethod.Eyedrop));
    }

    // ---- DescribeAllocation ----

    [Fact]
    public void DescribeAllocation_BothParts_AreJoined()
    {
        Assert.Equal("after meal · eyedrop", MedPlanRules.DescribeAllocation(MealRelation.AfterMeal, MedMethod.Eyedrop));
    }

    [Fact]
    public void DescribeAllocation_NoMealRelation_LeavesTheMethodAlone()
    {
        Assert.Equal("eyedrop", MedPlanRules.DescribeAllocation(MealRelation.None, MedMethod.Eyedrop));
    }

    [Fact]
    public void DescribeAllocation_MethodOther_LeavesTheMealRelationAlone()
    {
        Assert.Equal("before meal", MedPlanRules.DescribeAllocation(MealRelation.BeforeMeal, MedMethod.Other));
    }

    [Fact]
    public void DescribeAllocation_NothingToSay_IsEmpty()
    {
        // The row then shows its name and slots only — no empty separator hanging under it.
        Assert.Empty(MedPlanRules.DescribeAllocation(MealRelation.None, MedMethod.Other));
    }

    // ---- ComposeNote ----

    [Fact]
    public void ComposeNote_ReadsSlotThenMealThenMethod()
    {
        Assert.Equal(
            "morning · after meal · eyedrop",
            MedPlanRules.ComposeNote(MedSlots.Morning, MealRelation.AfterMeal, MedMethod.Eyedrop));
    }

    [Fact]
    public void ComposeNote_NoMealRelation_IsOmitted()
    {
        Assert.Equal("bedtime · eat", MedPlanRules.ComposeNote(MedSlots.Bedtime, MealRelation.None, MedMethod.Eat));
    }

    [Fact]
    public void ComposeNote_MethodOther_IsOmitted()
    {
        Assert.Equal("noon · with meal", MedPlanRules.ComposeNote(MedSlots.Noon, MealRelation.WithMeal, MedMethod.Other));
    }

    [Fact]
    public void ComposeNote_NothingButTheSlot_IsJustTheSlot()
    {
        Assert.Equal("evening", MedPlanRules.ComposeNote(MedSlots.Evening, MealRelation.None, MedMethod.Other));
    }

    [Fact]
    public void ComposeNote_EverySlotProducesANote()
    {
        // A tick always writes a note; an empty one would leave a bare name in the timeline.
        Assert.All(MedPlanRules.AllSlots, slot =>
            Assert.NotEmpty(MedPlanRules.ComposeNote(slot, MealRelation.None, MedMethod.Other)));
    }

    [Fact]
    public void ComposeNote_UsesTheSameSeparatorAsTheTimelineDetailLine()
    {
        // The note is rendered inside EntryRules.DetailLine, which joins with the same mark —
        // a different one here would read as two conventions on one line.
        var detail = EntryRules.DetailLine(
            BuiltInEntryTypes.Pill,
            null,
            "Eyedrop L",
            MedPlanRules.ComposeNote(MedSlots.Morning, MealRelation.AfterMeal, MedMethod.Eyedrop));

        Assert.Equal("Eyedrop L · morning · after meal · eyedrop", detail);
    }
}
