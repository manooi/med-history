using System.Globalization;
using MedHistory.Models;

namespace MedHistory.Services;

/// <summary>
/// The single place the app reads the clock and converts between the server's
/// local time (what the user sees and types) and the UTC instants stored in
/// Postgres. v1 treats server-local time as the user's time zone.
///
/// The formatting helpers split in two and the split is load-bearing. <b>Identity</b> —
/// <see cref="Key"/>, <see cref="InputValue"/>, <see cref="TimeInputValue"/>,
/// <see cref="TryParseDay"/> — is what a machine parses: URL segments and form values, always
/// <see cref="CultureInfo.InvariantCulture"/>. <b>Display</b> — <see cref="DayLabel"/>,
/// <see cref="TimeLabel"/> — is what a human reads and takes the culture as a parameter.
/// Routing an identifier through the display path breaks the app rather than merely looking
/// wrong: under th-TH the year is Buddhist-era, so a day key would read <c>2569-08-22</c> and
/// <see cref="TryParseDay"/> would read it straight back as the year 2569 — every link on the
/// page silently 543 years out, with nothing failing loudly enough to notice.
/// </summary>
public static class AppTime
{
    public const string DayFormat = "yyyy-MM-dd";

    private const string DateTimeInputFormat = "yyyy-MM-ddTHH:mm";

    private const string TimeInputFormat = "HH:mm";

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

    /// <summary>Instant a checklist tick is logged at — see <see cref="ChecklistRules.TickTime"/>.</summary>
    public static DateTimeOffset TickTime(DateOnly day, MedSlots slot) =>
        ChecklistRules.TickTime(day, Today(), DateTimeOffset.UtcNow, OffsetFor(day), slot);

    public static bool TryParseDay(string? value, out DateOnly day) =>
        DateOnly.TryParseExact(value, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out day);

    /// <summary>URL segment form of a day, e.g. <c>/day/2026-08-15</c>.</summary>
    public static string Key(DateOnly day) => day.ToString(DayFormat, CultureInfo.InvariantCulture);

    /// <summary>Value for an <c>&lt;input type="datetime-local"&gt;</c>.</summary>
    public static string InputValue(DateTime local) => local.ToString(DateTimeInputFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// Value for an <c>&lt;input type="time"&gt;</c>. Reads the same as <see cref="TimeLabel"/>
    /// does in every culture the app offers, and is still not it: this one is submitted back and
    /// parsed, so it is pinned invariant rather than left to follow whatever the reader's culture
    /// makes of a clock time.
    /// </summary>
    public static string TimeInputValue(DateTimeOffset instant) =>
        instant.ToString(TimeInputFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// How a day reads on screen, e.g. "Sat 15 Aug 2026" — under th-TH the day and month names
    /// are Thai and the year is Buddhist-era. The culture is a parameter rather than read from
    /// the ambient one so that a test pins it instead of inheriting the machine's; call sites
    /// pass <see cref="CultureInfo.CurrentUICulture"/>, which request localization has already
    /// set to the reader's choice.
    /// </summary>
    public static string DayLabel(DateOnly day, CultureInfo culture) =>
        day.ToString("ddd d MMM yyyy", culture);

    /// <summary>The clock time of an instant as a reader sees it — see <see cref="DayLabel"/>
    /// on why the culture is passed in.</summary>
    public static string TimeLabel(DateTimeOffset instant, CultureInfo culture) =>
        instant.ToString("HH:mm", culture);

    private static TimeSpan OffsetFor(DateOnly day) =>
        TimeZoneInfo.Local.GetUtcOffset(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified));
}
