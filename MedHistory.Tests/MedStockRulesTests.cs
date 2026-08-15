using MedHistory.Models;
using MedHistory.Services;

namespace MedHistory.Tests;

public class MedStockRulesTests
{
    private static MedStock Stock(int id, string name, decimal total) =>
        new() { Id = id, Name = name, TotalCount = total };

    /// <summary>Doses logged under one name, as they arrive pre-grouped from the database.</summary>
    private static MedUsage Usage(string? name, decimal quantity) => new(name, quantity);

    // ---- ValidateNewStock: name ----

    [Fact]
    public void ValidateNewStock_ANameAndATotal_IsAccepted()
    {
        Assert.Empty(MedStockRules.ValidateNewStock("Panadol", "30", [], out var total));
        Assert.Equal(30m, total);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateNewStock_NoName_IsRejected(string? name)
    {
        Assert.Single(MedStockRules.ValidateNewStock(name, "30", [], out _));
    }

    [Fact]
    public void ValidateNewStock_NameTooLong_IsRejected()
    {
        var name = new string('a', MedStockRules.NameMaxLength + 1);

        Assert.Single(MedStockRules.ValidateNewStock(name, "30", [], out _));
    }

    [Fact]
    public void ValidateNewStock_NameAtTheLimit_IsAccepted()
    {
        var name = new string('a', MedStockRules.NameMaxLength);

        Assert.Empty(MedStockRules.ValidateNewStock(name, "30", [], out _));
    }

    [Theory]
    [InlineData("Panadol")]
    [InlineData("panadol")]
    [InlineData("PANADOL")]
    [InlineData("  Panadol  ")]
    public void ValidateNewStock_ANameAlreadyStocked_IsRejectedHoweverItIsCased(string name)
    {
        // Two rows for one medication would split its count in half.
        Assert.Single(MedStockRules.ValidateNewStock(name, "30", ["Panadol"], out _));
    }

    [Fact]
    public void ValidateNewStock_ADifferentName_IsAccepted()
    {
        Assert.Empty(MedStockRules.ValidateNewStock("Eyedrop L", "30", ["Panadol"], out _));
    }

    [Fact]
    public void ValidateNewStock_BothNameAndTotalWrong_ReportsBoth()
    {
        Assert.Equal(2, MedStockRules.ValidateNewStock(null, "-1", [], out _).Count);
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

    // ---- DeriveRows ----

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
        var rows = MedStockRules.DeriveRows([Stock(1, "Panadol", 30m)], [Usage("Panadol", 12m)]);

        Assert.Equal(12m, rows[0].Consumed);
        Assert.Equal(18m, rows[0].Remaining);
    }

    [Theory]
    [InlineData("panadol")]
    [InlineData("PANADOL")]
    [InlineData("  Panadol  ")]
    public void DeriveRows_UsageIsMatchedIgnoringCaseAndSpacing(string logged)
    {
        var rows = MedStockRules.DeriveRows([Stock(1, "Panadol", 30m)], [Usage(logged, 5m)]);

        Assert.Equal(5m, rows[0].Consumed);
    }

    [Fact]
    public void DeriveRows_UsageArrivingAsSeveralSpellings_IsFoldedIntoOneRow()
    {
        // The database groups by the name as stored, so one medication reaches this as several
        // rows whenever it was typed differently. Folding them is this function's job.
        var rows = MedStockRules.DeriveRows(
            [Stock(1, "Panadol", 30m)],
            [Usage("Panadol", 5m), Usage("panadol", 3m), Usage(" PANADOL ", 2m)]);

        Assert.Equal(10m, rows[0].Consumed);
        Assert.Equal(20m, rows[0].Remaining);
    }

    [Fact]
    public void DeriveRows_HalfDoses_AreCountedAsHalves()
    {
        var rows = MedStockRules.DeriveRows(
            [Stock(1, "Panadol", 10m)],
            [Usage("Panadol", 0.5m), Usage("panadol", 0.25m)]);

        Assert.Equal(0.75m, rows[0].Consumed);
        Assert.Equal(9.25m, rows[0].Remaining);
    }

    [Fact]
    public void DeriveRows_MoreLoggedThanStocked_GoesNegative()
    {
        // Shown as it comes out: a count behind reality is information, not an error.
        var rows = MedStockRules.DeriveRows([Stock(1, "Panadol", 5m)], [Usage("Panadol", 7m)]);

        Assert.Equal(-2m, rows[0].Remaining);
    }

    [Fact]
    public void DeriveRows_UsageOfSomethingUnstocked_IsDropped()
    {
        var rows = MedStockRules.DeriveRows(
            [Stock(1, "Panadol", 30m)],
            [Usage("Eyedrop L", 4m), Usage(null, 9m)]);

        Assert.Equal(0m, rows[0].Consumed);
    }

    [Fact]
    public void DeriveRows_EachStockCountsOnlyItsOwnDoses()
    {
        var rows = MedStockRules.DeriveRows(
            [Stock(1, "Panadol", 30m), Stock(2, "Eyedrop L", 10m)],
            [Usage("Panadol", 12m), Usage("Eyedrop L", 4m)]);

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
        Assert.Empty(MedStockRules.DeriveRows([], [Usage("Panadol", 5m)]));
    }

    // ---- UsageQuantity ----

    [Fact]
    public void UsageQuantity_NoQuantityRecorded_CountsAsOneUnit()
    {
        // Hand-typed Pill entries, and every dose ticked before quantities existed.
        Assert.Equal(1m, MedStockRules.UsageQuantity(null));
    }

    [Fact]
    public void UsageQuantity_AQuantityRecorded_CountsAsItself()
    {
        Assert.Equal(0.5m, MedStockRules.UsageQuantity(0.5m));
    }

    // ---- FindRemaining ----

    [Fact]
    public void FindRemaining_AStockOfThatName_IsWhatIsLeftOfIt()
    {
        Assert.Equal(18m, MedStockRules.FindRemaining([new MedStockRow(1, "Panadol", 30m, 12m)], "Panadol"));
    }

    [Theory]
    [InlineData("panadol")]
    [InlineData("  PANADOL ")]
    public void FindRemaining_MatchesIgnoringCaseAndSpacing(string name)
    {
        Assert.Equal(18m, MedStockRules.FindRemaining([new MedStockRow(1, "Panadol", 30m, 12m)], name));
    }

    [Fact]
    public void FindRemaining_NothingStocksThatName_IsNull()
    {
        // Null rather than zero: the row must say nothing at all, not claim it has run out.
        Assert.Null(MedStockRules.FindRemaining([new MedStockRow(1, "Panadol", 30m, 12m)], "Eyedrop L"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FindRemaining_NoNameToMatch_IsNull(string? name)
    {
        Assert.Null(MedStockRules.FindRemaining([new MedStockRow(1, "Panadol", 30m, 12m)], name));
    }

    [Fact]
    public void FindRemaining_NoStockAtAll_IsNull()
    {
        Assert.Null(MedStockRules.FindRemaining(null, "Panadol"));
        Assert.Null(MedStockRules.FindRemaining([], "Panadol"));
    }

    // ---- RemainingLabel ----

    [Fact]
    public void RemainingLabel_NothingStocked_IsEmpty()
    {
        Assert.Empty(MedStockRules.RemainingLabel(null));
    }

    [Theory]
    [InlineData(18, "(18 left)")]
    [InlineData(0, "(0 left)")]
    [InlineData(0.5, "(0.5 left)")]
    [InlineData(-2, "(-2 left)")]
    public void RemainingLabel_AStockedMedication_ReadsAsWhatIsLeft(double remaining, string expected)
    {
        Assert.Equal(expected, MedStockRules.RemainingLabel((decimal)remaining));
    }

    [Fact]
    public void RemainingLabel_ScaleFromTheDatabaseIsNotShown()
    {
        // A numeric(7,2) total minus a summed consumption arrives as 18.00.
        Assert.Equal("(18 left)", MedStockRules.RemainingLabel(18.00m));
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
