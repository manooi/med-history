using MedHistory.Models;
using MedHistory.Services;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Data;

/// <summary>
/// The database side of adding and removing a weight reading from the day card. Both actions are
/// day-scoped: an id has to belong to <em>this</em> day, and Kind=Weight, before either reads or
/// touches it, so a hand-edited URL cannot reach into another day's row or a differently-kinded
/// measurement.
/// </summary>
public static class WeightStore
{
    public static async Task AddWeightAsync(this AppDbContext db, DateOnly day, TimeOnly time, decimal value)
    {
        db.Measurements.Add(new Measurement
        {
            Kind = MeasurementKinds.Weight,
            Value = value,
            OccurredAt = AppTime.FromLocal(day.ToDateTime(time))
        });

        await db.SaveChangesAsync();
    }

    /// <summary>Deletes the reading if it exists, is Kind=Weight, and falls on <paramref name="day"/>;
    /// otherwise does nothing — the same double-submit tolerance every other delete in the app has.</summary>
    public static async Task DeleteWeightAsync(this AppDbContext db, DateOnly day, int id)
    {
        var (start, end) = AppTime.DayRange(day);

        var measurement = await db.Measurements.SingleOrDefaultAsync(m =>
            m.Id == id && m.Kind == MeasurementKinds.Weight && m.OccurredAt >= start && m.OccurredAt < end);

        if (measurement is null)
        {
            return;
        }

        db.Measurements.Remove(measurement);
        await db.SaveChangesAsync();
    }
}
