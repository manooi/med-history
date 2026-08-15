using System.Globalization;

namespace MedHistory.Services;

/// <summary>
/// The single place the app reads the clock and converts between the server's
/// local time (what the user sees and types) and the UTC instants stored in
/// Postgres. v1 treats server-local time as the user's time zone.
/// </summary>
public static class AppTime
{
    public const string DayFormat = "yyyy-MM-dd";

    private const string DateTimeInputFormat = "yyyy-MM-ddTHH:mm";

    public static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);

    public static DateTimeOffset ToLocal(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.Local);

    public static DateOnly DayOf(DateTimeOffset instant) =>
        DateOnly.FromDateTime(ToLocal(instant).DateTime);

    /// <summary>Interprets a wall-clock time typed by the user as a UTC instant.</summary>
    public static DateTimeOffset FromLocal(DateTime local)
    {
        var wallClock = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(wallClock, TimeZoneInfo.Local.GetUtcOffset(wallClock)).ToUniversalTime();
    }

    public static (DateTimeOffset Start, DateTimeOffset End) DayRange(DateOnly day) =>
        EntryRules.LocalDayRange(day, OffsetFor(day));

    public static bool TryParseDay(string? value, out DateOnly day) =>
        DateOnly.TryParseExact(value, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out day);

    /// <summary>URL segment form of a day, e.g. <c>/day/2026-08-15</c>.</summary>
    public static string Key(DateOnly day) => day.ToString(DayFormat, CultureInfo.InvariantCulture);

    public static string DayLabel(DateOnly day) => day.ToString("ddd d MMM yyyy", CultureInfo.InvariantCulture);

    public static string TimeLabel(DateTimeOffset instant) => instant.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Value for an <c>&lt;input type="datetime-local"&gt;</c>.</summary>
    public static string InputValue(DateTime local) => local.ToString(DateTimeInputFormat, CultureInfo.InvariantCulture);

    private static TimeSpan OffsetFor(DateOnly day) =>
        TimeZoneInfo.Local.GetUtcOffset(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified));
}
