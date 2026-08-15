using MedHistory.Services;

namespace MedHistory.Tests;

public class LoginThrottleRulesTests
{
    private static readonly DateTime Now = new(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);

    private static DateTime MinutesAgo(int minutes) => Now.AddMinutes(-minutes);

    // ---- Decide: under the threshold ----

    [Fact]
    public void Decide_NoFailures_Allowed()
    {
        Assert.IsType<LoginThrottleDecision.Allowed>(LoginThrottleRules.Decide([], Now));
    }

    [Fact]
    public void Decide_FourFailures_Allowed()
    {
        var failures = Enumerable.Range(1, 4).Select(MinutesAgo).ToList();

        Assert.IsType<LoginThrottleDecision.Allowed>(LoginThrottleRules.Decide(failures, Now));
    }

    // ---- Decide: at the threshold ----

    [Fact]
    public void Decide_FiveStraightFailuresInWindow_LockedWithExactExpiry()
    {
        // Oldest of the five is 5 minutes ago; the lockout lifts 15 minutes after it.
        var failures = new List<DateTime> { MinutesAgo(5), MinutesAgo(4), MinutesAgo(3), MinutesAgo(2), MinutesAgo(1) };

        var decision = LoginThrottleRules.Decide(failures, Now);

        var lockedUntil = Assert.IsType<LoginThrottleDecision.LockedUntil>(decision);
        Assert.Equal(MinutesAgo(5).AddMinutes(LoginThrottleRules.WindowMinutes), lockedUntil.UntilUtc);
    }

    [Fact]
    public void Decide_MoreThanFiveFailures_UsesOnlyTheNewestFiveForExpiry()
    {
        // A sixth, older failure exists but must not push the expiry further out — only the
        // most recent MaxFailures failures set the threshold.
        var failures = new List<DateTime>
        {
            MinutesAgo(14), MinutesAgo(5), MinutesAgo(4), MinutesAgo(3), MinutesAgo(2), MinutesAgo(1)
        };

        var decision = LoginThrottleRules.Decide(failures, Now);

        var lockedUntil = Assert.IsType<LoginThrottleDecision.LockedUntil>(decision);
        Assert.Equal(MinutesAgo(5).AddMinutes(LoginThrottleRules.WindowMinutes), lockedUntil.UntilUtc);
    }

    // ---- Decide: failures aging out of the window ----

    [Fact]
    public void Decide_FiveFailuresButOnlyFourInWindow_Allowed()
    {
        // A caller is expected to pre-filter by CutoffUtc, but Decide itself only counts what it
        // is handed — five total, one of them (16 minutes old) already outside the 15-minute
        // window, leaves only four that matter.
        var failures = new List<DateTime> { MinutesAgo(16), MinutesAgo(4), MinutesAgo(3), MinutesAgo(2), MinutesAgo(1) };

        Assert.IsType<LoginThrottleDecision.Allowed>(LoginThrottleRules.Decide(failures, Now));
    }

    // ---- Decide: boundary at expiry ----

    [Fact]
    public void Decide_ExactlyAtLockedUntilExpiry_Allowed()
    {
        var failures = new List<DateTime> { MinutesAgo(5), MinutesAgo(4), MinutesAgo(3), MinutesAgo(2), MinutesAgo(1) };
        var lockedUntil = MinutesAgo(5).AddMinutes(LoginThrottleRules.WindowMinutes);

        Assert.IsType<LoginThrottleDecision.Allowed>(LoginThrottleRules.Decide(failures, lockedUntil));
    }

    [Fact]
    public void Decide_OneTickBeforeLockedUntilExpiry_StillLocked()
    {
        var failures = new List<DateTime> { MinutesAgo(5), MinutesAgo(4), MinutesAgo(3), MinutesAgo(2), MinutesAgo(1) };
        var lockedUntil = MinutesAgo(5).AddMinutes(LoginThrottleRules.WindowMinutes);

        Assert.IsType<LoginThrottleDecision.LockedUntil>(
            LoginThrottleRules.Decide(failures, lockedUntil.AddTicks(-1)));
    }

    // ---- CutoffUtc ----

    [Fact]
    public void CutoffUtc_IsExactlyWindowMinutesBeforeNow()
    {
        Assert.Equal(Now.AddMinutes(-LoginThrottleRules.WindowMinutes), LoginThrottleRules.CutoffUtc(Now));
    }

    [Fact]
    public void CutoffUtc_MatchesTheWindowUsedByDecide()
    {
        // A failure sitting right at the cutoff is exactly the oldest one still countable —
        // consistent with how a caller would filter AttemptedAtUtc >= CutoffUtc(now).
        var cutoff = LoginThrottleRules.CutoffUtc(Now);
        Assert.Equal(Now, cutoff.AddMinutes(LoginThrottleRules.WindowMinutes));
    }
}
