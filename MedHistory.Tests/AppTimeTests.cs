using System.Globalization;
using MedHistory.Services;

namespace MedHistory.Tests;

// AppTime.Today() reads DateTime.Now and is intentionally not covered here — it has
// no deterministic output to assert against. Every test below is deterministic given
// its inputs; the only environment dependency is TimeZoneInfo.Local, which several
// tests deliberately compare against rather than hard-coding an assumed offset, so
// they hold on any machine's configured time zone.
//
// The formatting tests come in two flavours, matching the split AppTime itself draws.
// Display helpers are handed a culture, so the assertion says which one it is instead
// of depending on the machine's. Identity helpers take no culture, so the way to test
// them is to make the ambient one hostile — th-TH, whose calendar is Buddhist-era —
// and check the output did not move.

public class AppTimeTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private static readonly CultureInfo Thai = CultureInfo.GetCultureInfo("th-TH");

    // ---- TryParseDay ----

    [Fact]
    public void TryParseDay_ValidFormat_ReturnsTrueWithParsedDay()
    {
        var success = AppTime.TryParseDay("2026-08-15", out var day);

        Assert.True(success);
        Assert.Equal(new DateOnly(2026, 8, 15), day);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-date")]
    [InlineData("2026/08/15")]
    [InlineData("08-15-2026")]
    [InlineData("2026-13-01")]
    [InlineData("2026-08-32")]
    public void TryParseDay_InvalidInput_ReturnsFalse(string? value)
    {
        var success = AppTime.TryParseDay(value, out var day);

        Assert.False(success);
        Assert.Equal(default, day);
    }

    [Theory]
    [InlineData(2026, 8, 15)]
    [InlineData(2026, 1, 1)]
    [InlineData(2024, 2, 29)] // leap day
    [InlineData(2026, 12, 31)]
    public void Key_TryParseDay_RoundTrips(int year, int month, int day)
    {
        var original = new DateOnly(year, month, day);

        var key = AppTime.Key(original);
        var parsed = AppTime.TryParseDay(key, out var result);

        Assert.True(parsed);
        Assert.Equal(original, result);
    }

    [Fact]
    public void Key_FormatsAsYyyyMmDd()
    {
        var key = AppTime.Key(new DateOnly(2026, 8, 5));

        Assert.Equal("2026-08-05", key);
    }

    // ---- DayOf ----

    [Fact]
    public void DayOf_SameInstant_DifferentSourceOffsets_ReturnsSameDay()
    {
        // The calendar day should depend only on the absolute instant, not on which
        // offset the caller happened to express it in.
        var utcInstant = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        var plusSeven = utcInstant.ToOffset(TimeSpan.FromHours(7));
        var minusFive = utcInstant.ToOffset(TimeSpan.FromHours(-5));

        var dayFromUtc = AppTime.DayOf(utcInstant);
        var dayFromPlusSeven = AppTime.DayOf(plusSeven);
        var dayFromMinusFive = AppTime.DayOf(minusFive);

        Assert.Equal(dayFromUtc, dayFromPlusSeven);
        Assert.Equal(dayFromUtc, dayFromMinusFive);
    }

    [Fact]
    public void DayOf_MatchesLocalTimeZoneConversion()
    {
        var instant = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        var expectedDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.Local).DateTime);

        var day = AppTime.DayOf(instant);

        Assert.Equal(expectedDay, day);
    }

    // ---- FromLocal / ToLocal roundtrip ----

    [Fact]
    public void FromLocal_ToLocal_RoundTrips()
    {
        var local = new DateTime(2026, 8, 15, 14, 30, 0, DateTimeKind.Unspecified);

        var instant = AppTime.FromLocal(local);
        var roundTripped = AppTime.ToLocal(instant);

        Assert.Equal(local, roundTripped.DateTime);
    }

    [Fact]
    public void FromLocal_ReturnsUtcInstant()
    {
        var local = new DateTime(2026, 8, 15, 14, 30, 0, DateTimeKind.Unspecified);

        var instant = AppTime.FromLocal(local);

        Assert.Equal(TimeSpan.Zero, instant.Offset);
    }

    [Fact]
    public void ToLocal_UsesLocalTimeZoneOffset()
    {
        var instant = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

        var local = AppTime.ToLocal(instant);

        Assert.Equal(TimeZoneInfo.Local.GetUtcOffset(instant), local.Offset);
    }

    // ---- DayRange (wiring onto EntryRules.LocalDayRange) ----

    [Fact]
    public void DayRange_StartCorrespondsToGivenLocalDay_AndIsOneDayWide()
    {
        var day = new DateOnly(2026, 8, 15);

        var (start, end) = AppTime.DayRange(day);

        Assert.Equal(day, AppTime.DayOf(start));
        Assert.Equal(start.AddDays(1), end);
    }

    // ---- DayLabel / TimeLabel (display: the culture is an argument) ----

    [Fact]
    public void DayLabel_InEnglish_FormatsAsDayOfWeekDayMonthYear()
    {
        var label = AppTime.DayLabel(new DateOnly(2026, 8, 15), English); // a Saturday

        Assert.Equal("Sat 15 Aug 2026", label);
    }

    [Fact]
    public void DayLabel_InThai_ReadsTheBuddhistEraYear()
    {
        var label = AppTime.DayLabel(new DateOnly(2026, 8, 15), Thai);

        // 2026 + 543. The Thai day and month names are ICU's to spell and are not pinned here;
        // the era is the part that would be silently wrong if the label went invariant.
        Assert.Contains("2569", label);
        Assert.DoesNotContain("2026", label);
        Assert.DoesNotContain("Aug", label);
    }

    [Fact]
    public void TimeLabel_InEnglish_FormatsAsHHmm()
    {
        var instant = new DateTimeOffset(2026, 8, 15, 9, 5, 0, TimeSpan.FromHours(3));

        var label = AppTime.TimeLabel(instant, English);

        Assert.Equal("09:05", label);
    }

    [Fact]
    public void TimeLabel_InThai_KeepsTheSameDigitsAndSeparator()
    {
        // Thai is written with Arabic numerals and a colon, so a clock time reads identically in
        // both languages. Asserted rather than assumed: it is why nothing in the app has to lay
        // out times differently per culture.
        var instant = new DateTimeOffset(2026, 8, 15, 9, 5, 0, TimeSpan.FromHours(3));

        var label = AppTime.TimeLabel(instant, Thai);

        Assert.Equal("09:05", label);
    }

    // ---- Key / InputValue / TimeInputValue / TryParseDay (identity: invariant whatever the
    //      ambient culture is) ----

    [Fact]
    public void Key_UnderThaiAmbientCulture_StaysGregorianIso()
    {
        using var culture = new CultureScope("th-TH");

        var key = AppTime.Key(new DateOnly(2026, 8, 15));

        Assert.Equal("2026-08-15", key);
    }

    [Fact]
    public void Key_TryParseDay_RoundTripUnderThaiAmbientCulture()
    {
        // The failure this guards is quiet: a Buddhist-era key parses back cleanly as the year
        // 2569, so the app would follow its own links 543 years out rather than error.
        using var culture = new CultureScope("th-TH");

        var day = new DateOnly(2026, 8, 15);

        Assert.True(AppTime.TryParseDay(AppTime.Key(day), out var parsed));
        Assert.Equal(day, parsed);
    }

    [Fact]
    public void InputValue_FormatsAsDateTimeLocalInputValue()
    {
        var local = new DateTime(2026, 8, 15, 9, 5, 0);

        var value = AppTime.InputValue(local);

        Assert.Equal("2026-08-15T09:05", value);
    }

    [Fact]
    public void InputValue_UnderThaiAmbientCulture_StaysGregorianIso()
    {
        using var culture = new CultureScope("th-TH");

        var value = AppTime.InputValue(new DateTime(2026, 8, 15, 9, 5, 0));

        Assert.Equal("2026-08-15T09:05", value);
    }

    [Fact]
    public void TimeInputValue_FormatsAsTimeInputValue()
    {
        var instant = new DateTimeOffset(2026, 8, 15, 9, 5, 0, TimeSpan.FromHours(3));

        var value = AppTime.TimeInputValue(instant);

        Assert.Equal("09:05", value);
    }

    [Fact]
    public void TimeInputValue_UnderThaiAmbientCulture_StaysHHmm()
    {
        using var culture = new CultureScope("th-TH");

        var instant = new DateTimeOffset(2026, 8, 15, 9, 5, 0, TimeSpan.FromHours(3));

        Assert.Equal("09:05", AppTime.TimeInputValue(instant));
    }

    [Fact]
    public void CultureScope_PutsTheAmbientCultureBack()
    {
        // Every identity test above leans on this, so it is asserted rather than assumed: a scope
        // that leaked would hand its culture to whatever xUnit ran next on the same thread.
        var before = CultureInfo.CurrentCulture;

        using (var culture = new CultureScope("th-TH"))
        {
            Assert.Equal("th-TH", CultureInfo.CurrentCulture.Name);
        }

        Assert.Equal(before, CultureInfo.CurrentCulture);
    }
}
