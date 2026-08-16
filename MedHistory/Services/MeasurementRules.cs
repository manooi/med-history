using System.Globalization;
using MedHistory.Models;

namespace MedHistory.Services;

/// <summary>The kinds of <see cref="Measurement"/> the app knows about so far. Kind itself is a
/// plain string column — see the comment on <see cref="Measurement.Kind"/> — these constants just
/// keep every reader of one spelling it the same way.</summary>
public static class MeasurementKinds
{
    public const string Weight = "Weight";
}

/// <summary>One measurement reduced to what a month report reads: the local day it falls on, its
/// value, and the instant it was recorded. The instant matters only for picking the later of two
/// same-day readings — see <see cref="MeasurementRules.BuildMonth"/>.</summary>
public readonly record struct MeasurementReading(DateOnly Day, decimal Value, DateTimeOffset OccurredAt);

/// <summary>One day of a measurement report: the day, and its latest reading's value, if any. A
/// day with no reading is not a missing cell — every real day of the month gets one, see
/// <see cref="MeasurementRules.BuildMonth"/> — it is a cell whose <see cref="Value"/> is null.</summary>
public readonly record struct MeasurementDay(DateOnly Day, decimal? Value);

/// <summary>One row of the calendar: seven cells, Monday first — the same shape every calendar
/// report in the app shares, built on <see cref="ReportRules.BuildWeeks{TCell}"/>.</summary>
public readonly record struct MeasurementWeek(IReadOnlyList<MeasurementDay?> Days);

/// <summary>
/// A month as a measurement report renders it: the grid, and the month's own stats. The stats
/// come back null when the month has no reading at all — there is nothing to average, and zero
/// would misread as a reading of zero.
/// </summary>
public sealed record MeasurementMonth(
    DateOnly FirstDay,
    IReadOnlyList<MeasurementWeek> Weeks,
    decimal? Min,
    decimal? Max,
    decimal? Average,
    int MeasuredCount)
{
    public string Key => ReportRules.MonthKey(FirstDay);

    public string Label => ReportRules.MonthLabel(FirstDay);

    public string PreviousKey => ReportRules.MonthKey(FirstDay.AddMonths(-1));

    public string NextKey => ReportRules.MonthKey(FirstDay.AddMonths(1));
}

/// <summary>
/// Pure measurement rules — no clock, no database, no HTTP. A measurement is a value typed into a
/// number-ish input, so parsing it the same tolerant, non-throwing way <see cref="MedPlanRules"/>
/// parses a dose quantity is this file's other half.
/// </summary>
public static class MeasurementRules
{
    /// <summary>Longest a stored <see cref="Measurement.Kind"/> is.</summary>
    public const int KindMaxLength = 32;

    /// <summary>
    /// A value must be strictly between these, so "0", "1000" and anything negative are all
    /// rejected — a ceiling loose enough for any real reading, tight enough to catch a typo.
    /// </summary>
    public const decimal MinValue = 0m;

    public const decimal MaxValue = 1000m;

    /// <summary>Most decimal places a value may carry.</summary>
    public const int MaxDecimalPlaces = 2;

    /// <summary>
    /// Reads a value typed into a number-ish input. Invariant culture, decimal point only — no
    /// thousands separator, so a comma decimal ("70,5", meant as seventy and a half in a
    /// comma-decimal locale) fails outright rather than silently reading as seven hundred and
    /// five. Same non-throwing stance as <see cref="MedPlanRules.TryParseQuantity"/>: false for
    /// anything not a plain positive decimal with at most two decimal places, blank included.
    /// </summary>
    public static bool TryParseValue(string? raw, out decimal value)
    {
        if (!decimal.TryParse(
                raw?.Trim(),
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out value)
            || value <= MinValue
            || value >= MaxValue
            || DecimalPlaces(value) > MaxDecimalPlaces)
        {
            value = default;
            return false;
        }

        return true;
    }

    /// <summary>
    /// A value as it reads on screen: no trailing zeros, so a <c>numeric(5,2)</c> column that
    /// comes back as 72.50 still shows as "72.5". Invariant so the point matches what a form
    /// would post back.
    /// </summary>
    public static string FormatValue(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// The whole report for one month: the grid and its stats. Readings outside the month
    /// contribute nothing, so a caller that over-fetches cannot bend the month's numbers — the
    /// same guarantee <see cref="ReportRules.BuildMonth"/> gives the med report and
    /// <see cref="AnxietyRules.BuildMonth"/> gives the anxiety report. Two readings on the same
    /// day collapse to one cell: the later one, by <see cref="MeasurementReading.OccurredAt"/>,
    /// since a later reading is presumed to supersede an earlier same-day one rather than average
    /// against it.
    /// </summary>
    public static MeasurementMonth BuildMonth(DateOnly month, IEnumerable<MeasurementReading> readings)
    {
        var first = ReportRules.FirstOfMonth(month);

        var byDay = readings
            .Where(reading => ReportRules.FirstOfMonth(reading.Day) == first)
            .GroupBy(reading => reading.Day)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(reading => reading.OccurredAt).First().Value);

        var weeks = ReportRules
            .BuildWeeks<MeasurementDay>(
                first, day => new MeasurementDay(day, byDay.TryGetValue(day, out var value) ? value : null))
            .Select(week => new MeasurementWeek(week))
            .ToList();

        var values = byDay.Values;

        return new MeasurementMonth(
            first,
            weeks,
            values.Count == 0 ? null : values.Min(),
            values.Count == 0 ? null : values.Max(),
            values.Count == 0 ? null : Math.Round(values.Average(), 1, MidpointRounding.AwayFromZero),
            values.Count);
    }

    /// <summary>Digits after the decimal point a parsed value carries, trailing zeros included —
    /// "1.50" reads as two places, same as "1.53".</summary>
    private static int DecimalPlaces(decimal value)
    {
        var bits = decimal.GetBits(value);
        return (bits[3] >> 16) & 0x7F;
    }
}
