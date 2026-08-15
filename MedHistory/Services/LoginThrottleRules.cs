namespace MedHistory.Services;

/// <summary>Whether a login POST may check the password at all — see
/// <see cref="LoginThrottleRules.Decide"/>. Locked carries the instant the lockout lifts, not a
/// remaining-time span, so the caller decides how to phrase it against its own clock read.</summary>
public abstract record LoginThrottleDecision
{
    public sealed record Allowed : LoginThrottleDecision;

    public sealed record LockedUntil(DateTime UntilUtc) : LoginThrottleDecision;
}

/// <summary>
/// Pure login-throttling rules — no clock, no database. A straight failure streak locks the
/// account out; a success is never recorded as a failure, so streaks only ever end by expiring
/// out of the window or by <c>AccountController.Login</c> clearing the table outright on success.
/// </summary>
public static class LoginThrottleRules
{
    /// <summary>How far back a failure still counts toward the lockout.</summary>
    public const int WindowMinutes = 15;

    /// <summary>Straight failures within the window that trigger a lockout.</summary>
    public const int MaxFailures = 5;

    /// <summary>Imposed on every wrong-password POST, locked out or not — makes a brute-force
    /// script pay a fixed cost per guess instead of only after it trips the lockout.</summary>
    public static readonly TimeSpan FailDelay = TimeSpan.FromSeconds(2);

    /// <summary>The query bound for "failures that still count" as of <paramref name="nowUtc"/> —
    /// what a caller loading recent failures should filter <c>AttemptedAtUtc &gt;=</c> against.
    /// </summary>
    public static DateTime CutoffUtc(DateTime nowUtc) => nowUtc.AddMinutes(-WindowMinutes);

    /// <summary>
    /// Whether a login POST may proceed to check the password. <paramref name="recentFailuresUtc"/>
    /// should already be filtered to <see cref="CutoffUtc"/> — this does not re-filter, so a caller
    /// that over-fetches would over-count. Locked out once <see cref="MaxFailures"/> or more
    /// failures fall in the window; the lockout lifts <see cref="WindowMinutes"/> after the oldest
    /// failure among the most recent <see cref="MaxFailures"/>, which is the earliest instant a
    /// fresh window can no longer see that many failures.
    /// </summary>
    public static LoginThrottleDecision Decide(IReadOnlyList<DateTime> recentFailuresUtc, DateTime nowUtc)
    {
        if (recentFailuresUtc.Count < MaxFailures)
        {
            return new LoginThrottleDecision.Allowed();
        }

        var thresholdFailure = recentFailuresUtc
            .OrderByDescending(failure => failure)
            .Take(MaxFailures)
            .Min();

        var lockedUntil = thresholdFailure.AddMinutes(WindowMinutes);

        return lockedUntil > nowUtc
            ? new LoginThrottleDecision.LockedUntil(lockedUntil)
            : new LoginThrottleDecision.Allowed();
    }
}
