using MedHistory.Models;
using MedHistory.Services;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Data;

/// <summary>
/// The database side of login throttling: reading, recording and clearing failed attempts.
/// <see cref="MedHistory.Controllers.AccountController"/> keeps the throttle decision, the
/// password check and the fixed per-guess delay visible — a security flow that reads
/// top-to-bottom in the controller — and calls in here only for the raw EF reads and writes
/// behind it.
/// </summary>
public static class LoginThrottleStore
{
    /// <summary>
    /// Every failure timestamp still inside the lookback window as of <paramref name="now"/>,
    /// for <see cref="LoginThrottleRules.Decide"/> — already filtered to
    /// <see cref="LoginThrottleRules.CutoffUtc"/> so the caller does not have to re-filter.
    /// </summary>
    public static async Task<List<DateTime>> RecentFailureTimesAsync(this AppDbContext db, DateTime now) =>
        await db.LoginAttempts
            .AsNoTracking()
            .Where(a => !a.Succeeded && a.AttemptedAtUtc >= LoginThrottleRules.CutoffUtc(now))
            .Select(a => a.AttemptedAtUtc)
            .ToListAsync();

    /// <summary>Records a wrong-password attempt at <paramref name="now"/>.</summary>
    public static async Task RecordFailureAsync(this AppDbContext db, DateTime now)
    {
        db.LoginAttempts.Add(new LoginAttempt { AttemptedAtUtc = now, Succeeded = false });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Wipes the failure streak outright on a successful login, rather than recording a success
    /// row — see the comment on LoginAttempt.
    /// </summary>
    public static async Task ClearAsync(this AppDbContext db) =>
        await db.LoginAttempts.ExecuteDeleteAsync();
}
