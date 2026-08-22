using MedHistory.Models;
using MedHistory.Services;

namespace MedHistory.Tests;

public class MedStockRulesTests
{
    private static MedStock Stock(int id, string name, decimal total) =>
        new() { Id = id, Name = name, TotalCount = total };

    /// <summary>
    /// Doses typed in by hand: a name and nothing else, so they can only find a stock by name.
    /// </summary>
    private static MedUsage Manual(string? name, decimal quantity) => new(null, name, quantity);

    /// <summary>
    /// Doses ticked off the checklist: stamped with the id of the stock they came out of, and
    /// carrying whatever the medication was called at the time.
    /// </summary>
    private static MedUsage Ticked(int stockId, string? name, decimal quantity) =>
        new(stockId, name, quantity);

    // ---- ValidateStock: name ----

    [Fact]
    public void ValidateStock_ANameAndATotal_IsAccepted()
    {
        Assert.Empty(MedStockRules.ValidateStock("Panadol", "30", [], out var total));
        Assert.Equal(30m, total);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateStock_NoName_IsRejected(string? name)
    {
        Assert.Single(MedStockRules.ValidateStock(name, "30", [], out _));
    }

    [Fact]
    public void ValidateStock_NameTooLong_IsRejected()
    {
        var name = new string('a', MedStockRules.NameMaxLength + 1);

        Assert.Single(MedStockRules.ValidateStock(name, "30", [], out _));
    }

    [Fact]
    public void ValidateStock_NameAtTheLimit_IsAccepted()
    {
        var name = new string('a', MedStockRules.NameMaxLength);

        Assert.Empty(MedStockRules.ValidateStock(name, "30", [], out _));
    }

    [Theory]
    [InlineData("Panadol")]
    [InlineData("panadol")]
    [InlineData("PANADOL")]
    [InlineData("  Panadol  ")]
    public void ValidateStock_ANameAlreadyStocked_IsRejectedHoweverItIsCased(string name)
    {
        // Two rows for one medication would split its count in half.
        Assert.Single(MedStockRules.ValidateStock(name, "30", ["Panadol"], out _));
    }

    [Fact]
    public void ValidateStock_ADifferentName_IsAccepted()
    {
        Assert.Empty(MedStockRules.ValidateStock("Eyedrop L", "30", ["Panadol"], out _));
    }

    [Fact]
    public void ValidateStock_BothNameAndTotalWrong_ReportsBoth()
    {
        Assert.Equal(2, MedStockRules.ValidateStock(null, "-1", [], out _).Count);
    }

    [Fact]
    public void ValidateStock_ARenameToAFreeName_IsAccepted()
    {
        // On an edit the caller passes every stocked name but the row's own, so the row is
        // judged against its neighbours and never against itself.
        Assert.Empty(MedStockRules.ValidateStock("Panadol Extra", "30", ["Eyedrop L"], out _));
    }

    [Fact]
    public void ValidateStock_AnEditThatLeavesTheNameAlone_IsNotADuplicateOfItself()
    {
        // The row's own name is absent from otherNames, which is what makes saving a refill
        // without touching the name work at all.
        Assert.Empty(MedStockRules.ValidateStock("Panadol", "60", ["Eyedrop L"], out _));
    }

    [Fact]
    public void ValidateStock_ARenameOntoAnotherRowsName_IsRejected()
    {
        Assert.Single(MedStockRules.ValidateStock("eyedrop l", "30", ["Eyedrop L"], out _));
    }

    // ---- ValidateTotal ----

    [Theory]
    [InlineData("0", 0)]
    [InlineData("30", 30)]
    [InlineData("12.5", 12.5)]
    [InlineData("0.25", 0.25)]
    [InlineData("99999.99", 99999.99)]
    public void ValidateTotal_InRange_IsAccepted(string raw, double expected)
    {
        // Zero included: "I have run out" is worth recording.
        Assert.Empty(MedStockRules.ValidateTotal(raw, out var total));
        Assert.Equal((decimal)expected, total);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateTotal_Missing_IsRejected(string? raw)
    {
        Assert.Single(MedStockRules.ValidateTotal(raw, out _));
    }

    [Theory]
    [InlineData("many")]
    [InlineData("30 tablets")]
    public void ValidateTotal_NotANumber_IsRejected(string raw)
    {
        Assert.Single(MedStockRules.ValidateTotal(raw, out _));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("-0.01")]
    [InlineData("100000")]
    public void ValidateTotal_OutsideTheBounds_IsRejected(string raw)
    {
        // A negative total is a count of what was bought, so it cannot be below zero — what is
        // left of it may well be.
        Assert.Single(MedStockRules.ValidateTotal(raw, out _));
    }

    [Theory]
    [InlineData("1.005")]
    [InlineData("30.123")]
    public void ValidateTotal_MoreDecimalsThanTheColumnKeeps_IsRejectedRatherThanRounded(string raw)
    {
        Assert.Single(MedStockRules.ValidateTotal(raw, out _));
    }

    [Fact]
    public void ValidateTotal_Rejected_LeavesNoTotalToStore()
    {
        MedStockRules.ValidateTotal("nonsense", out var total);

        Assert.Equal(0m, total);
    }

    // ---- DeriveRows: hand-typed doses, which carry no link and are matched by name ----

    [Fact]
    public void DeriveRows_NoUsage_LeavesTheWholeTotal()
    {
        var rows = MedStockRules.DeriveRows([Stock(1, "Panadol", 30m)], []);

        Assert.Equal(0m, rows[0].Consumed);
        Assert.Equal(30m, rows[0].Remaining);
    }

    [Fact]
    public void DeriveRows_UsageIsSubtractedFromTheTotal()
    {
        var rows = MedStockRules.DeriveRows([Stock(1, "Panadol", 30m)], [Manual("Panadol", 12m)]);

        Assert.Equal(12m, rows[0].Consumed);
        Assert.Equal(18m, rows[0].Remaining);
    }

    [Theory]
    [InlineData("panadol")]
    [InlineData("PANADOL")]
    [InlineData("  Panadol  ")]
    public void DeriveRows_UsageIsMatchedIgnoringCaseAndSpacing(string logged)
    {
        var rows = MedStockRules.DeriveRows([Stock(1, "Panadol", 30m)], [Manual(logged, 5m)]);

        Assert.Equal(5m, rows[0].Consumed);
    }

    [Fact]
    public void DeriveRows_UsageArrivingAsSeveralSpellings_IsFoldedIntoOneRow()
    {
        // The database groups by the name as stored, so one medication reaches this as several
        // rows whenever it was typed differently. Folding them is this function's job.
        var rows = MedStockRules.DeriveRows(
            [Stock(1, "Panadol", 30m)],
            [Manual("Panadol", 5m), Manual("panadol", 3m), Manual(" PANADOL ", 2m)]);

        Assert.Equal(10m, rows[0].Consumed);
        Assert.Equal(20m, rows[0].Remaining);
    }

    [Fact]
    public void DeriveRows_HalfDoses_AreCountedAsHalves()
    {
        var rows = MedStockRules.DeriveRows(
            [Stock(1, "Panadol", 10m)],
            [Manual("Panadol", 0.5m), Manual("panadol", 0.25m)]);

        Assert.Equal(0.75m, rows[0].Consumed);
        Assert.Equal(9.25m, rows[0].Remaining);
    }

    [Fact]
    public void DeriveRows_MoreLoggedThanStocked_GoesNegative()
    {
        // Shown as it comes out: a count behind reality is information, not an error.
        var rows = MedStockRules.DeriveRows([Stock(1, "Panadol", 5m)], [Manual("Panadol", 7m)]);

        Assert.Equal(-2m, rows[0].Remaining);
    }

    [Fact]
    public void DeriveRows_UsageOfSomethingUnstocked_IsDropped()
    {
        var rows = MedStockRules.DeriveRows(
            [Stock(1, "Panadol", 30m)],
            [Manual("Eyedrop L", 4m), Manual(null, 9m)]);

        Assert.Equal(0m, rows[0].Consumed);
    }

    [Fact]
    public void DeriveRows_EachStockCountsOnlyItsOwnDoses()
    {
        var rows = MedStockRules.DeriveRows(
            [Stock(1, "Panadol", 30m), Stock(2, "Eyedrop L", 10m)],
            [Manual("Panadol", 12m), Manual("Eyedrop L", 4m)]);

        Assert.Equal(18m, rows[0].Remaining);
        Assert.Equal(6m, rows[1].Remaining);
    }

    [Fact]
    public void DeriveRows_KeepsTheOrderAndIdentityOfTheStocksGiven()
    {
        var rows = MedStockRules.DeriveRows(
            [Stock(7, "Panadol", 30m), Stock(3, "Eyedrop L", 10m)],
            []);

        Assert.Equal([7, 3], rows.Select(r => r.Id));
        Assert.Equal(["Panadol", "Eyedrop L"], rows.Select(r => r.Name));
    }

    [Fact]
    public void DeriveRows_NoStocks_ReturnsEmpty()
    {
        Assert.Empty(MedStockRules.DeriveRows([], [Manual("Panadol", 5m)]));
    }

    // ---- DeriveRows: ticked doses, which carry a link and are matched by id ----

    [Fact]
    public void DeriveRows_ALinkedDose_IsCountedAgainstTheStockItNames()
    {
        var rows = MedStockRules.DeriveRows([Stock(1, "Panadol", 30m)], [Ticked(1, "Panadol", 12m)]);

        Assert.Equal(12m, rows[0].Consumed);
        Assert.Equal(18m, rows[0].Remaining);
    }

    [Fact]
    public void DeriveRows_RenamingAStock_KeepsTheDosesTickedAgainstIt()
    {
        // The bug this linkage exists to fix. The doses were logged as "Panadol" and the stock
        // row is now called something else; joined by name they would all fall off and the
        // remaining count would jump back to the full total.
        var rows = MedStockRules.DeriveRows(
            [Stock(1, "Panadol Extra", 30m)],
            [Ticked(1, "Panadol", 12m)]);

        Assert.Equal(12m, rows[0].Consumed);
        Assert.Equal(18m, rows[0].Remaining);
    }

    [Fact]
    public void DeriveRows_RenamingThePlanForward_KeepsTheDosesAlreadyTicked()
    {
        // Doses ticked under the old plan name and doses ticked under the new one carry the same
        // stock id, so they arrive as two groups and land on the same row.
        var rows = MedStockRules.DeriveRows(
            [Stock(1, "Panadol", 30m)],
            [Ticked(1, "Panadol", 8m), Ticked(1, "Panadol 500", 4m)]);

        Assert.Equal(12m, rows[0].Consumed);
    }

    [Fact]
    public void DeriveRows_ALinkedDose_IsNotAlsoCountedByItsName()
    {
        // Its name matches this very row, so counting both routes would double it.
        var rows = MedStockRules.DeriveRows([Stock(1, "Panadol", 30m)], [Ticked(1, "Panadol", 12m)]);

        Assert.Equal(12m, rows[0].Consumed);
    }

    [Fact]
    public void DeriveRows_ALinkedDose_IgnoresARowMerelySharingItsName()
    {
        // The id wins outright: the dose came out of stock 1 and counts there, even though the
        // name it was logged with now belongs to stock 2.
        var rows = MedStockRules.DeriveRows(
            [Stock(1, "Panadol Extra", 30m), Stock(2, "Panadol", 20m)],
            [Ticked(1, "Panadol", 6m)]);

        Assert.Equal(6m, rows[0].Consumed);
        Assert.Equal(0m, rows[1].Consumed);
    }

    [Fact]
    public void DeriveRows_ADanglingLink_CountsTowardNothing()
    {
        // The stock row it pointed at was removed. The dose is still a real entry; it simply
        // draws down nothing that is tracked any more, and never falls back to its name.
        var rows = MedStockRules.DeriveRows(
            [Stock(1, "Panadol", 30m)],
            [Ticked(99, "Panadol", 7m)]);

        Assert.Equal(0m, rows[0].Consumed);
        Assert.Equal(30m, rows[0].Remaining);
    }

    [Fact]
    public void DeriveRows_LinkedAndManualDoses_AreBothCounted()
    {
        // A dose typed in by hand is as real as one ticked, whichever route it takes here.
        var rows = MedStockRules.DeriveRows(
            [Stock(1, "Panadol", 30m)],
            [Ticked(1, "Panadol", 8m), Manual("panadol", 2m)]);

        Assert.Equal(10m, rows[0].Consumed);
    }

    [Fact]
    public void DeriveRows_RenamingAStock_MovesTheManualDosesWithTheName()
    {
        // The documented asymmetry: a hand-typed dose has only a name, so it follows whatever
        // the row is called now and stops counting once the row is called something else.
        var rows = MedStockRules.DeriveRows(
            [Stock(1, "Panadol Extra", 30m)],
            [Ticked(1, "Panadol", 8m), Manual("Panadol", 2m)]);

        Assert.Equal(8m, rows[0].Consumed);
    }

    // ---- UsageQuantity ----

    [Fact]
    public void UsageQuantity_NoQuantityRecorded_CountsAsOneUnit()
    {
        // Hand-typed Med entries, and every dose ticked before quantities existed.
        Assert.Equal(1m, MedStockRules.UsageQuantity(null));
    }

    [Fact]
    public void UsageQuantity_AQuantityRecorded_CountsAsItself()
    {
        Assert.Equal(0.5m, MedStockRules.UsageQuantity(0.5m));
    }

    // ---- ResolveStockId ----

    [Fact]
    public void ResolveStockId_AStockOfThatName_IsItsId()
    {
        Assert.Equal(3, MedStockRules.ResolveStockId(
            [Stock(3, "Panadol", 30m), Stock(4, "Eyedrop L", 10m)], "Panadol"));
    }

    [Theory]
    [InlineData("panadol")]
    [InlineData("  PANADOL ")]
    public void ResolveStockId_MatchesIgnoringCaseAndSpacing(string name)
    {
        Assert.Equal(3, MedStockRules.ResolveStockId([Stock(3, "Panadol", 30m)], name));
    }

    [Fact]
    public void ResolveStockId_NothingStocksThatName_IsNull()
    {
        // Not an error: a medication the user never counts simply draws on no tracked supply.
        Assert.Null(MedStockRules.ResolveStockId([Stock(3, "Panadol", 30m)], "Eyedrop L"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveStockId_NoNameToMatch_IsNull(string? name)
    {
        Assert.Null(MedStockRules.ResolveStockId([Stock(3, "Panadol", 30m)], name));
    }

    [Fact]
    public void ResolveStockId_NothingStockedAtAll_IsNull()
    {
        Assert.Null(MedStockRules.ResolveStockId([], "Panadol"));
    }

    // ---- Relink ----

    [Fact]
    public void Relink_LinksThatStillAgreeWithTheNames_AreLeftAlone()
    {
        // Only what changes is returned, so a caller writes nothing when nothing moved.
        var changed = MedStockRules.Relink(
            [new StockLink(10, "Panadol", 1)],
            [Stock(1, "Panadol", 30m)]);

        Assert.Empty(changed);
    }

    [Fact]
    public void Relink_AnAllocationNamingANewlyStockedMedication_GainsTheLink()
    {
        var changed = MedStockRules.Relink(
            [new StockLink(10, "Panadol", null)],
            [Stock(1, "Panadol", 30m)]);

        Assert.Equal([new StockLink(10, "Panadol", 1)], changed);
    }

    [Fact]
    public void Relink_AnAllocationWhoseStockWasRemoved_LosesTheLink()
    {
        var changed = MedStockRules.Relink(
            [new StockLink(10, "Panadol", 1)],
            []);

        Assert.Equal([new StockLink(10, "Panadol", null)], changed);
    }

    [Fact]
    public void Relink_ARenamedStock_MovesTheLinkFromOneAllocationToAnother()
    {
        // One edit renamed stock 1 from "Panadol" to "Panadol Extra": the allocation that named
        // the old name drops its link and the one naming the new name picks it up. Sweeping every
        // allocation is what catches both halves of a single rename.
        var changed = MedStockRules.Relink(
            [new StockLink(10, "Panadol", 1), new StockLink(11, "Panadol Extra", null)],
            [Stock(1, "Panadol Extra", 30m)]);

        Assert.Equal(
            [new StockLink(10, "Panadol", null), new StockLink(11, "Panadol Extra", 1)],
            changed);
    }

    [Fact]
    public void Relink_MatchesIgnoringCaseAndSpacing()
    {
        Assert.Empty(MedStockRules.Relink(
            [new StockLink(10, "  panadol ", 1)],
            [Stock(1, "Panadol", 30m)]));
    }

    [Fact]
    public void Relink_NothingStocked_ClearsEveryLinkAndLeavesUnlinkedRowsAlone()
    {
        var changed = MedStockRules.Relink(
            [new StockLink(10, "Panadol", 1), new StockLink(11, "Eyedrop L", null)],
            []);

        Assert.Equal([new StockLink(10, "Panadol", null)], changed);
    }

    // ---- FindRemaining ----

    [Fact]
    public void FindRemaining_AStockOfThatName_IsWhatIsLeftOfIt()
    {
        Assert.Equal(18m, MedStockRules.FindRemaining([new MedStockRow(1, "Panadol", 30m, 12m)], null, "Panadol"));
    }

    [Theory]
    [InlineData("panadol")]
    [InlineData("  PANADOL ")]
    public void FindRemaining_MatchesIgnoringCaseAndSpacing(string name)
    {
        Assert.Equal(18m, MedStockRules.FindRemaining([new MedStockRow(1, "Panadol", 30m, 12m)], null, name));
    }

    [Fact]
    public void FindRemaining_NothingStocksThatName_IsNull()
    {
        // Null rather than zero: the row must say nothing at all, not claim it has run out.
        Assert.Null(MedStockRules.FindRemaining([new MedStockRow(1, "Panadol", 30m, 12m)], null, "Eyedrop L"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FindRemaining_NoNameToMatch_IsNull(string? name)
    {
        Assert.Null(MedStockRules.FindRemaining([new MedStockRow(1, "Panadol", 30m, 12m)], null, name));
    }

    [Fact]
    public void FindRemaining_NoStockAtAll_IsNull()
    {
        Assert.Null(MedStockRules.FindRemaining(null, null, "Panadol"));
        Assert.Null(MedStockRules.FindRemaining([], null, "Panadol"));
    }

    [Fact]
    public void FindRemaining_ALink_ReadsThatRowWhateverItIsCalledNow()
    {
        // The plan still says "Panadol" and the stock row has been renamed; the link is what
        // keeps the checklist showing the same count the meds page shows.
        Assert.Equal(18m, MedStockRules.FindRemaining(
            [new MedStockRow(1, "Panadol Extra", 30m, 12m)], 1, "Panadol"));
    }

    [Fact]
    public void FindRemaining_ALink_IsPreferredOverARowSharingTheName()
    {
        Assert.Equal(18m, MedStockRules.FindRemaining(
            [new MedStockRow(1, "Panadol Extra", 30m, 12m), new MedStockRow(2, "Panadol", 50m, 0m)],
            1,
            "Panadol"));
    }

    [Fact]
    public void FindRemaining_ALinkToARowSinceRemoved_IsNullRatherThanFallingBackToTheName()
    {
        // It reads as unstocked, which is what it now is — the same stance the consumption
        // count takes on a dangling link.
        Assert.Null(MedStockRules.FindRemaining([new MedStockRow(2, "Panadol", 30m, 12m)], 1, "Panadol"));
    }

    // ---- RemainingLabel ----

    [Fact]
    public void RemainingLabel_NothingStocked_IsNoLabelAtAll()
    {
        // Null and not an empty key: there is nothing to look up, and "(0 left)" would claim the
        // medication had run out rather than that nothing stocks it.
        Assert.Null(MedStockRules.RemainingLabel(null));
    }

    [Theory]
    [InlineData(18, "18")]
    [InlineData(0, "0")]
    [InlineData(0.5, "0.5")]
    [InlineData(-2, "-2")]
    public void RemainingLabel_AStockedMedication_NamesTheKeyAndCarriesTheCount(
        double remaining, string expected)
    {
        // The brackets belong to the key, so the count arrives on its own and the translation
        // punctuates it — asserting "(18 left)" here would pin copy this no longer owns.
        var label = MedStockRules.RemainingLabel((decimal)remaining);

        Assert.Equal(MedStockRules.RemainingKey, label!.Value.Key);
        Assert.Equal([expected], label.Value.Args);
    }

    [Fact]
    public void RemainingLabel_ScaleFromTheDatabaseIsNotShown()
    {
        // A numeric(7,2) total minus a summed consumption arrives as 18.00.
        Assert.Equal("(18 left)", MedStockRules.RemainingLabel(18.00m)!.Value.Text);
    }

    // ---- Names are the same rule everywhere ----

    [Fact]
    public void NormalizeName_IsTheSameRuleAllocationNamesFollow()
    {
        Assert.Equal(ChecklistRules.NormalizeName("  Panadol  "), MedStockRules.NormalizeName("  Panadol  "));
        Assert.Null(MedStockRules.NormalizeName("   "));
    }

    [Fact]
    public void NamesMatch_IsTheSameRuleAllocationNamesFollow()
    {
        Assert.True(MedStockRules.NamesMatch("Panadol ", " panadol"));
        Assert.False(MedStockRules.NamesMatch("Panadol", "Eyedrop L"));
        Assert.False(MedStockRules.NamesMatch(null, null));
    }
}
