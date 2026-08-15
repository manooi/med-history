using MedHistory.Models;
using MedHistory.Services;

namespace MedHistory.Tests;

public class EntryRulesTests
{
    // ---- Validate: Severity requirement ----

    [Theory]
    [InlineData(EntryType.Bleeding)]
    [InlineData(EntryType.Cough)]
    public void Validate_MissingSeverity_OnTypeThatRequiresIt_ReturnsError(EntryType type)
    {
        var note = EntryRules.RequiresNote(type) ? "note" : null;

        var errors = EntryRules.Validate(type, severity: null, pillName: null, note: note);

        Assert.Contains(errors, e => e.Contains("Severity is required"));
    }

    [Theory]
    [InlineData(EntryType.Bleeding)]
    [InlineData(EntryType.Cough)]
    public void Validate_SeverityPresent_OnTypeThatRequiresIt_NoSeverityError(EntryType type)
    {
        var note = EntryRules.RequiresNote(type) ? "note" : null;

        var errors = EntryRules.Validate(type, severity: Severity.Light, pillName: null, note: note);

        Assert.DoesNotContain(errors, e => e.Contains("Severity"));
    }

    [Theory]
    [InlineData(EntryType.Symptom)]
    [InlineData(EntryType.Pill)]
    [InlineData(EntryType.Meal)]
    public void Validate_SeverityGiven_OnTypeThatDoesNotSupportIt_ReturnsError(EntryType type)
    {
        var note = EntryRules.RequiresNote(type) ? "note" : null;
        var pillName = EntryRules.RequiresPillName(type) ? "Aspirin" : null;

        var errors = EntryRules.Validate(type, severity: Severity.Light, pillName: pillName, note: note);

        Assert.Contains(errors, e => e.Contains("Severity does not apply"));
    }

    // ---- Validate: Pill name requirement ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_Pill_MissingOrWhitespacePillName_ReturnsError(string? pillName)
    {
        var errors = EntryRules.Validate(EntryType.Pill, severity: null, pillName: pillName, note: null);

        Assert.Contains(errors, e => e.Contains("Pill name is required"));
    }

    [Fact]
    public void Validate_Pill_WithPillName_NoPillNameError()
    {
        var errors = EntryRules.Validate(EntryType.Pill, severity: null, pillName: "Aspirin", note: null);

        Assert.DoesNotContain(errors, e => e.Contains("Pill name"));
    }

    [Theory]
    [InlineData(EntryType.Symptom)]
    [InlineData(EntryType.Bleeding)]
    [InlineData(EntryType.Cough)]
    [InlineData(EntryType.Meal)]
    public void Validate_PillNameGiven_OnTypeThatDoesNotSupportIt_ReturnsError(EntryType type)
    {
        var severity = EntryRules.RequiresSeverity(type) ? Severity.Light : (Severity?)null;
        var note = EntryRules.RequiresNote(type) ? "note" : null;

        var errors = EntryRules.Validate(type, severity: severity, pillName: "Aspirin", note: note);

        Assert.Contains(errors, e => e.Contains("Pill name does not apply"));
    }

    // ---- Validate: Note requirement ----

    [Theory]
    [InlineData(EntryType.Symptom)]
    [InlineData(EntryType.Meal)]
    public void Validate_MissingNote_OnTypeThatRequiresIt_ReturnsError(EntryType type)
    {
        var errors = EntryRules.Validate(type, severity: null, pillName: null, note: null);

        Assert.Contains(errors, e => e.Contains("Note is required"));
    }

    [Theory]
    [InlineData(EntryType.Symptom)]
    [InlineData(EntryType.Meal)]
    public void Validate_WhitespaceNote_OnTypeThatRequiresIt_ReturnsError(EntryType type)
    {
        var errors = EntryRules.Validate(type, severity: null, pillName: null, note: "   ");

        Assert.Contains(errors, e => e.Contains("Note is required"));
    }

    // ---- Validate: valid combos for all 5 types ----

    [Fact]
    public void Validate_Symptom_ValidCombo_NoErrors()
    {
        var errors = EntryRules.Validate(EntryType.Symptom, severity: null, pillName: null, note: "Headache");

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_Bleeding_ValidCombo_NoErrors()
    {
        var errors = EntryRules.Validate(EntryType.Bleeding, severity: Severity.Moderate, pillName: null, note: null);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_Pill_ValidCombo_NoErrors()
    {
        var errors = EntryRules.Validate(EntryType.Pill, severity: null, pillName: "Ibuprofen", note: null);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_Cough_ValidCombo_NoErrors()
    {
        var errors = EntryRules.Validate(EntryType.Cough, severity: Severity.Severe, pillName: null, note: null);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_Meal_ValidCombo_NoErrors()
    {
        var errors = EntryRules.Validate(EntryType.Meal, severity: null, pillName: null, note: "Rice and soup");

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_Bleeding_ValidCombo_WithOptionalNote_NoErrors()
    {
        // Note is not required for Bleeding, but supplying one should not error.
        var errors = EntryRules.Validate(EntryType.Bleeding, severity: Severity.Light, pillName: null, note: "spotting");

        Assert.Empty(errors);
    }

    // ---- RequiresSeverity / RequiresPillName / RequiresNote truth table ----

    [Theory]
    [InlineData(EntryType.Symptom, false, false, true)]
    [InlineData(EntryType.Bleeding, true, false, false)]
    [InlineData(EntryType.Pill, false, true, false)]
    [InlineData(EntryType.Cough, true, false, false)]
    [InlineData(EntryType.Meal, false, false, true)]
    public void RequirementFlags_MatchTruthTable(EntryType type, bool requiresSeverity, bool requiresPillName, bool requiresNote)
    {
        Assert.Equal(requiresSeverity, EntryRules.RequiresSeverity(type));
        Assert.Equal(requiresPillName, EntryRules.RequiresPillName(type));
        Assert.Equal(requiresNote, EntryRules.RequiresNote(type));
    }

    // ---- DetailLine ----

    [Fact]
    public void DetailLine_Pill_WithNameOnly_ReturnsTrimmedName()
    {
        var line = EntryRules.DetailLine(EntryType.Pill, severity: null, pillName: "  Aspirin  ", note: null);

        Assert.Equal("Aspirin", line);
    }

    [Fact]
    public void DetailLine_Bleeding_WithSeverityOnly_ReturnsSeverityText()
    {
        var line = EntryRules.DetailLine(EntryType.Bleeding, severity: Severity.Moderate, pillName: null, note: null);

        Assert.Equal("Moderate", line);
    }

    [Fact]
    public void DetailLine_ComposesePillSeverityAndNote_InOrder_SeparatedByMiddleDot()
    {
        // Pill entries don't carry severity per the domain rules, but DetailLine composes
        // whatever fields are present/applicable — exercise all three parts together.
        var line = EntryRules.DetailLine(EntryType.Pill, severity: null, pillName: "Aspirin", note: "with food");

        Assert.Equal("Aspirin · with food", line);
    }

    [Fact]
    public void DetailLine_Bleeding_SeverityAndNote_Composes()
    {
        var line = EntryRules.DetailLine(EntryType.Bleeding, severity: Severity.Severe, pillName: null, note: "heavy flow");

        Assert.Equal("Severe · heavy flow", line);
    }

    [Fact]
    public void DetailLine_NoteOnly_TrimsWhitespace()
    {
        var line = EntryRules.DetailLine(EntryType.Symptom, severity: null, pillName: null, note: "  Headache  ");

        Assert.Equal("Headache", line);
    }

    [Fact]
    public void DetailLine_AllPartsEmpty_ReturnsNull()
    {
        var line = EntryRules.DetailLine(EntryType.Symptom, severity: null, pillName: null, note: null);

        Assert.Null(line);
    }

    [Fact]
    public void DetailLine_AllPartsWhitespaceOnly_ReturnsNull()
    {
        var line = EntryRules.DetailLine(EntryType.Symptom, severity: null, pillName: "   ", note: "   ");

        Assert.Null(line);
    }

    [Fact]
    public void DetailLine_SeverityIgnored_WhenTypeDoesNotRequireIt()
    {
        // Symptom doesn't require severity, so even if a severity value were passed,
        // DetailLine should not surface it — only Note.
        var line = EntryRules.DetailLine(EntryType.Symptom, severity: Severity.Severe, pillName: null, note: "Headache");

        Assert.Equal("Headache", line);
    }

    [Fact]
    public void DetailLine_PillNameIgnored_WhenTypeDoesNotRequireIt()
    {
        var line = EntryRules.DetailLine(EntryType.Symptom, severity: null, pillName: "Aspirin", note: "Headache");

        Assert.Equal("Headache", line);
    }

    // ---- LocalDayRange ----

    [Fact]
    public void LocalDayRange_HalfOpenRange_StartInclusiveEndExclusive()
    {
        var day = new DateOnly(2026, 8, 15);
        var offset = TimeSpan.Zero;

        var (start, end) = EntryRules.LocalDayRange(day, offset);

        Assert.True(start < end);
        Assert.Equal(day, DateOnly.FromDateTime(start.UtcDateTime));
    }

    [Fact]
    public void LocalDayRange_NormalisesBoundsToUtc()
    {
        var day = new DateOnly(2026, 8, 15);
        var offset = TimeSpan.FromHours(7); // +07:00, e.g. Bangkok

        var (start, end) = EntryRules.LocalDayRange(day, offset);

        Assert.Equal(TimeSpan.Zero, start.Offset);
        Assert.Equal(TimeSpan.Zero, end.Offset);
    }

    [Fact]
    public void LocalDayRange_PositiveOffset_StartIsPreviousUtcDay()
    {
        // 2026-08-15 00:00 +07:00 == 2026-08-14 17:00 UTC.
        var day = new DateOnly(2026, 8, 15);
        var offset = TimeSpan.FromHours(7);

        var (start, _) = EntryRules.LocalDayRange(day, offset);

        Assert.Equal(new DateTimeOffset(2026, 8, 14, 17, 0, 0, TimeSpan.Zero), start);
    }

    [Fact]
    public void LocalDayRange_EndEqualsStartPlusOneDay()
    {
        var day = new DateOnly(2026, 8, 15);
        var offset = TimeSpan.FromHours(7);

        var (start, end) = EntryRules.LocalDayRange(day, offset);

        Assert.Equal(start.AddDays(1), end);
    }

    [Fact]
    public void LocalDayRange_NegativeOffset_ComputesCorrectUtcBounds()
    {
        // 2026-08-15 00:00 -05:00 == 2026-08-15 05:00 UTC.
        var day = new DateOnly(2026, 8, 15);
        var offset = TimeSpan.FromHours(-5);

        var (start, end) = EntryRules.LocalDayRange(day, offset);

        Assert.Equal(new DateTimeOffset(2026, 8, 15, 5, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2026, 8, 16, 5, 0, 0, TimeSpan.Zero), end);
    }
}
