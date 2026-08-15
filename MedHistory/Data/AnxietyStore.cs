using MedHistory.Models;
using MedHistory.Services;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Data;

/// <summary>
/// The database side of an anxiety vote: applying whatever <see cref="AnxietyRules.DecideVote"/>
/// already decided. <see cref="MedHistory.Controllers.DayController"/> keeps the decision itself
/// visible — this is called only once the action to take is already known.
/// </summary>
public static class AnxietyStore
{
    /// <summary>
    /// Clears, adds or updates the day's vote per <paramref name="action"/>, then saves. A
    /// unique-index violation on <c>Day</c> is swallowed rather than left to 500: the check the
    /// caller made before calling in only loses to the index when two first-votes for the same
    /// day race, and that race is worth losing quietly — the winning request already recorded
    /// the vote, and PRG makes the loser's redirect harmless. Same pattern as
    /// StocksController.AddStock.
    /// </summary>
    public static async Task ApplyVoteAsync(
        this AppDbContext db, DateOnly day, AnxietyVote? existing, AnxietyLevel requested, VoteAction action)
    {
        if (action == VoteAction.Clear)
        {
            db.AnxietyVotes.Remove(existing!);
        }
        else if (existing is null)
        {
            db.AnxietyVotes.Add(new AnxietyVote { Day = day, Level = requested });
        }
        else
        {
            existing.Level = requested;
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // The unique index on Day is the real guard; the check the caller made only beats it
            // if two votes for the same not-yet-voted day race, which is worth losing quietly
            // rather than a 500 — the winning request already recorded the vote, and PRG makes
            // the loser's redirect harmless. Same pattern as StocksController.AddStock.
        }
    }
}
