using MedHistory.Services;

namespace MedHistory.Tests;

public class DoctorReportRulesTests
{
    private static readonly DateOnly Today = new(2026, 8, 15);

    // ---- ResolveRange: defaults ----

    [Fact]
    public void ResolveRange_BothMissing_DefaultsToLast30Days()
    {
        var (from, to) = DoctorReportRules.ResolveRange(null, null, Today);

        Assert.Equal(new DateOnly(2026, 7, 17), from);
        Assert.Equal(Today, to);
        Assert.Equal(30, DoctorReportRules.TotalDays(from, to));
    }

    [Theory]
    [InlineData(null, "2026-08-01")]
    [InlineData("2026-08-01", null)]
    [InlineData("garbage", "2026-08-01")]
    [InlineData("2026-08-01", "garbage")]
    [InlineData("2026-08-32", "2026-08-01")]
    [InlineData("", "")]
    public void ResolveRange_EitherBoundMissingOrUnparsable_DefaultsEntirely(string? fromRaw, string? toRaw)
    {
        // A typed bound paired with a garbage one is not half-honoured — the whole range falls
        // back to default rather than mixing a real bound with a guessed one.
        var (from, to) = DoctorReportRules.ResolveRange(fromRaw, toRaw, Today);

        Assert.Equal(new DateOnly(2026, 7, 17), from);
        Assert.Equal(Today, to);
    }

    // ---- ResolveRange: swap ----

    [Fact]
    public void ResolveRange_FromAfterTo_Swaps()
    {
        var (from, to) = DoctorReportRules.ResolveRange("2026-08-10", "2026-08-01", Today);

        Assert.Equal(new DateOnly(2026, 8, 1), from);
        Assert.Equal(new DateOnly(2026, 8, 10), to);
    }

    [Fact]
    public void ResolveRange_SameDayBothBounds_IsAOneDayRange()
    {
        var (from, to) = DoctorReportRules.ResolveRange("2026-08-01", "2026-08-01", Today);

        Assert.Equal(new DateOnly(2026, 8, 1), from);
        Assert.Equal(new DateOnly(2026, 8, 1), to);
        Assert.Equal(1, DoctorReportRules.TotalDays(from, to));
    }

    // ---- ResolveRange: clamp ----

    [Fact]
    public void ResolveRange_ExactlyMaxRangeDays_IsNotClamped()
    {
        var to = new DateOnly(2026, 8, 15);
        var from = to.AddDays(-(DoctorReportRules.MaxRangeDays - 1));

        var (resolvedFrom, resolvedTo) = DoctorReportRules.ResolveRange(
            AppTime.Key(from), AppTime.Key(to), Today);

        Assert.Equal(from, resolvedFrom);
        Assert.Equal(to, resolvedTo);
        Assert.Equal(DoctorReportRules.MaxRangeDays, DoctorReportRules.TotalDays(resolvedFrom, resolvedTo));
    }

    [Fact]
    public void ResolveRange_OneDayOverMaxRangeDays_ClampsFromForward()
    {
        var to = new DateOnly(2026, 8, 15);
        var from = to.AddDays(-DoctorReportRules.MaxRangeDays); // one day past the max range

        var (resolvedFrom, resolvedTo) = DoctorReportRules.ResolveRange(
            AppTime.Key(from), AppTime.Key(to), Today);

        // To never moves — clamping pulls From forward instead.
        Assert.Equal(to, resolvedTo);
        Assert.Equal(to.AddDays(-(DoctorReportRules.MaxRangeDays - 1)), resolvedFrom);
        Assert.Equal(DoctorReportRules.MaxRangeDays, DoctorReportRules.TotalDays(resolvedFrom, resolvedTo));
    }

    [Fact]
    public void ResolveRange_ClampAppliesAfterSwap_UsesTheLaterDateAsTo()
    {
        // Given backwards and too long, the swap happens first, then the clamp pins From to
        // whichever bound ended up as To — so this is not two independent behaviours colliding.
        var later = new DateOnly(2026, 8, 15);
        var earlier = later.AddDays(-(DoctorReportRules.MaxRangeDays + 10));

        var (from, to) = DoctorReportRules.ResolveRange(AppTime.Key(later), AppTime.Key(earlier), Today);

        Assert.Equal(later, to);
        Assert.Equal(later.AddDays(-(DoctorReportRules.MaxRangeDays - 1)), from);
    }

    // ---- TotalDays ----

    [Fact]
    public void TotalDays_SameDay_IsOne()
    {
        var day = new DateOnly(2026, 8, 1);
        Assert.Equal(1, DoctorReportRules.TotalDays(day, day));
    }

    [Fact]
    public void TotalDays_IsInclusiveOfBothEnds()
    {
        Assert.Equal(10, DoctorReportRules.TotalDays(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 10)));
    }

    // ---- TypeCounts ----

    [Fact]
    public void TypeCounts_TalliesEachTypeSeparately()
    {
        var counts = DoctorReportRules.TypeCounts(["Symptom", "Bleeding", "Symptom", "Symptom"]);

        Assert.Equal(2, counts.Count);
        Assert.Contains(counts, c => c.Type == "Symptom" && c.Count == 3);
        Assert.Contains(counts, c => c.Type == "Bleeding" && c.Count == 1);
    }

    [Fact]
    public void TypeCounts_OrdersBuiltInsBeforeCustomTypes()
    {
        // Built-ins come out in their seed order regardless of the order they were counted in;
        // a user-added type sorts after every built-in, alphabetically among its own kind.
        var counts = DoctorReportRules.TypeCounts(["Meal", "Symptom", "Zzz", "Bleeding"]);

        Assert.Equal(["Symptom", "Bleeding", "Meal", "Zzz"], counts.Select(c => c.Type).ToList());
    }

    [Fact]
    public void TypeCounts_EmptyInput_IsEmpty()
    {
        Assert.Empty(DoctorReportRules.TypeCounts([]));
    }

    [Fact]
    public void TypeCounts_NoZeroRows_OnlyTypesActuallyLogged()
    {
        var counts = DoctorReportRules.TypeCounts(["Symptom"]);

        Assert.Single(counts);
        Assert.DoesNotContain(counts, c => c.Type == "Bleeding");
    }

    // ---- VotedDayCount ----

    [Fact]
    public void VotedDayCount_CountsOnlyDaysInsideTheRange()
    {
        var from = new DateOnly(2026, 8, 1);
        var to = new DateOnly(2026, 8, 10);

        var count = DoctorReportRules.VotedDayCount(
            [new DateOnly(2026, 7, 31), new DateOnly(2026, 8, 5), new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 11)],
            from,
            to);

        // 7/31 and 8/11 fall outside [from, to] and must not be counted.
        Assert.Equal(2, count);
    }

    [Fact]
    public void VotedDayCount_BoundsAreInclusive()
    {
        var day = new DateOnly(2026, 8, 1);

        Assert.Equal(1, DoctorReportRules.VotedDayCount([day], day, day));
    }

    [Fact]
    public void VotedDayCount_NoVotes_IsZero()
    {
        Assert.Equal(0, DoctorReportRules.VotedDayCount([], new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 10)));
    }
}
