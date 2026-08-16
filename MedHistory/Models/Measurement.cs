namespace MedHistory.Models;

/// <summary>
/// One reading of some quantity tracked over time — weight today, potentially blood pressure or
/// similar later. <see cref="Kind"/> is a plain string, not an enum: a generic table one new kind
/// can join by adding a constant, not a schema change. See
/// <see cref="Services.MeasurementKinds"/> for the kinds that exist so far.
/// </summary>
public class Measurement
{
    public int Id { get; set; }

    /// <summary>Which quantity this reading is of, e.g. <see cref="Services.MeasurementKinds.Weight"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    public decimal Value { get; set; }

    /// <summary>The instant the reading was taken, or the day/time it was retro-logged for.</summary>
    public DateTimeOffset OccurredAt { get; set; }
}
