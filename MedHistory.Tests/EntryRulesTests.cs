using MedHistory.Models;
using MedHistory.Services;

namespace MedHistory.Tests;

public class EntryRulesTests
{
    // A type added from the /types page: name only, no type-specific fields.
    private const string CustomType = "Blood pressure";

    // ---- Validate: Severity requirement ----

    [Theory]
    [InlineData(BuiltInEntryTypes.Bleeding)]
    [InlineData(BuiltInEntryTypes.Cough)]
    public void Validate_MissingSeverity_OnTypeThatRequiresIt_ReturnsError(string type)
    {
        var note = EntryRules.RequiresNote(type) ? "note" : null;

        var errors = EntryRules.Validate(type, severity: null, pillName: null, note: note);

        Assert.Contains(errors, e => e.Contains("Severity is required"));
    }

    [Theory]
    [InlineData(BuiltInEntryTypes.Bleeding)]
    [InlineData(BuiltInEntryTypes.Cough)]
    public void Validate_SeverityPresent_OnTypeThatRequiresIt_NoSeverityError(string type)
    {
        var note = EntryRules.RequiresNote(type) ? "note" : null;

        var errors = EntryRules.Validate(type, severity: Severity.Light, pillName: null, note: note);

        Assert.DoesNotContain(errors, e => e.Contains("Severity"));
    }

    [Theory]
    [InlineData(BuiltInEntryTypes.Symptom)]
    [InlineData(BuiltInEntryTypes.Pill)]
    [InlineData(BuiltInEntryTypes.Meal)]
    [InlineData(CustomType)]
    public void Validate_SeverityGiven_OnTypeThatDoesNotSupportIt_ReturnsError(string type)
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
        var errors = EntryRules.Validate(BuiltInEntryTypes.Pill, severity: null, pillName: pillName, note: null);

        Assert.Contains(errors, e => e.Contains("Pill name is required"));
    }

    [Fact]
    public void Validate_Pill_WithPillName_NoPillNameError()
    {
        var errors = EntryRules.Validate(BuiltInEntryTypes.Pill, severity: null, pillName: "Aspirin", note: null);

        Assert.DoesNotContain(errors, e => e.Contains("Pill name"));
    }

    [Theory]
    [InlineData(BuiltInEntryTypes.Symptom)]
    [InlineData(BuiltInEntryTypes.Bleeding)]
    [InlineData(BuiltInEntryTypes.Cough)]
    [InlineData(BuiltInEntryTypes.Meal)]
    [InlineData(CustomType)]
    public void Validate_PillNameGiven_OnTypeThatDoesNotSupportIt_ReturnsError(string type)
    {
        var severity = EntryRules.RequiresSeverity(type) ? Severity.Light : (Severity?)null;
        var note = EntryRules.RequiresNote(type) ? "note" : null;

        var errors = EntryRules.Validate(type, severity: severity, pillName: "Aspirin", note: note);

        Assert.Contains(errors, e => e.Contains("Pill name does not apply"));
    }

    // ---- Validate: Note requirement ----

    [Theory]
    [InlineData(BuiltInEntryTypes.Symptom)]
    [InlineData(BuiltInEntryTypes.Meal)]
    public void Validate_MissingNote_OnTypeThatRequiresIt_ReturnsError(string type)
    {
        var errors = EntryRules.Validate(type, severity: null, pillName: null, note: null);

        Assert.Contains(errors, e => e.Contains("Note is required"));
    }

    [Theory]
    [InlineData(BuiltInEntryTypes.Symptom)]
    [InlineData(BuiltInEntryTypes.Meal)]
    public void Validate_WhitespaceNote_OnTypeThatRequiresIt_ReturnsError(string type)
    {
        var errors = EntryRules.Validate(type, severity: null, pillName: null, note: "   ");

        Assert.Contains(errors, e => e.Contains("Note is required"));
    }

    // ---- Validate: valid combos for all 5 built-ins ----

    [Fact]
    public void Validate_Symptom_ValidCombo_NoErrors()
    {
        var errors = EntryRules.Validate(BuiltInEntryTypes.Symptom, severity: null, pillName: null, note: "Headache");

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_Bleeding_ValidCombo_NoErrors()
    {
        var errors = EntryRules.Validate(BuiltInEntryTypes.Bleeding, severity: Severity.Moderate, pillName: null, note: null);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_Pill_ValidCombo_NoErrors()
    {
        var errors = EntryRules.Validate(BuiltInEntryTypes.Pill, severity: null, pillName: "Ibuprofen", note: null);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_Cough_ValidCombo_NoErrors()
    {
        var errors = EntryRules.Validate(BuiltInEntryTypes.Cough, severity: Severity.Severe, pillName: null, note: null);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_Meal_ValidCombo_NoErrors()
    {
        var errors = EntryRules.Validate(BuiltInEntryTypes.Meal, severity: null, pillName: null, note: "Rice and soup");

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_Bleeding_ValidCombo_WithOptionalNote_NoErrors()
    {
        // Note is not required for Bleeding, but supplying one should not error.
        var errors = EntryRules.Validate(BuiltInEntryTypes.Bleeding, severity: Severity.Light, pillName: null, note: "spotting");

        Assert.Empty(errors);
    }

    // ---- Validate: custom (user-added) types ----

    [Fact]
    public void Validate_CustomType_NoFieldsAtAll_NoErrors()
    {
        // A custom type carries nothing but a timestamp, an optional note and photos.
        var errors = EntryRules.Validate(CustomType, severity: null, pillName: null, note: null);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_CustomType_WithNote_NoErrors()
    {
        var errors = EntryRules.Validate(CustomType, severity: null, pillName: null, note: "128/82");

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_CustomType_NamedLikeABuiltInButDifferentCase_TreatedAsCustom()
    {
        // Type names are stored and compared in their canonical casing; a stray casing
        // never inherits a built-in's fields. The /types page prevents such a row existing.
        var errors = EntryRules.Validate("bleeding", severity: null, pillName: null, note: null);

        Assert.Empty(errors);
    }

    // ---- RequiresSeverity / RequiresPillName / RequiresNote truth table ----

    [Theory]
    [InlineData(BuiltInEntryTypes.Symptom, false, false, true)]
    [InlineData(BuiltInEntryTypes.Bleeding, true, false, false)]
    [InlineData(BuiltInEntryTypes.Pill, false, true, false)]
    [InlineData(BuiltInEntryTypes.Cough, true, false, false)]
    [InlineData(BuiltInEntryTypes.Meal, false, false, true)]
    [InlineData(CustomType, false, false, false)]
    [InlineData("Mood", false, false, false)]
    public void RequirementFlags_MatchTruthTable(string type, bool requiresSeverity, bool requiresPillName, bool requiresNote)
    {
        Assert.Equal(requiresSeverity, EntryRules.RequiresSeverity(type));
        Assert.Equal(requiresPillName, EntryRules.RequiresPillName(type));
        Assert.Equal(requiresNote, EntryRules.RequiresNote(type));
    }

    // ---- DetailLine ----

    [Fact]
    public void DetailLine_Pill_WithNameOnly_ReturnsTrimmedName()
    {
        var line = EntryRules.DetailLine(BuiltInEntryTypes.Pill, severity: null, pillName: "  Aspirin  ", note: null);

        Assert.Equal("Aspirin", line);
    }

    [Fact]
    public void DetailLine_Bleeding_WithSeverityOnly_ReturnsSeverityText()
    {
        var line = EntryRules.DetailLine(BuiltInEntryTypes.Bleeding, severity: Severity.Moderate, pillName: null, note: null);

        Assert.Equal("Moderate", line);
    }

    [Fact]
    public void DetailLine_ComposesePillSeverityAndNote_InOrder_SeparatedByMiddleDot()
    {
        // Pill entries don't carry severity per the domain rules, but DetailLine composes
        // whatever fields are present/applicable — exercise all three parts together.
        var line = EntryRules.DetailLine(BuiltInEntryTypes.Pill, severity: null, pillName: "Aspirin", note: "with food");

        Assert.Equal("Aspirin · with food", line);
    }

    [Fact]
    public void DetailLine_Bleeding_SeverityAndNote_Composes()
    {
        var line = EntryRules.DetailLine(BuiltInEntryTypes.Bleeding, severity: Severity.Severe, pillName: null, note: "heavy flow");

        Assert.Equal("Severe · heavy flow", line);
    }

    [Fact]
    public void DetailLine_NoteOnly_TrimsWhitespace()
    {
        var line = EntryRules.DetailLine(BuiltInEntryTypes.Symptom, severity: null, pillName: null, note: "  Headache  ");

        Assert.Equal("Headache", line);
    }

    [Fact]
    public void DetailLine_AllPartsEmpty_ReturnsNull()
    {
        var line = EntryRules.DetailLine(BuiltInEntryTypes.Symptom, severity: null, pillName: null, note: null);

        Assert.Null(line);
    }

    [Fact]
    public void DetailLine_AllPartsWhitespaceOnly_ReturnsNull()
    {
        var line = EntryRules.DetailLine(BuiltInEntryTypes.Symptom, severity: null, pillName: "   ", note: "   ");

        Assert.Null(line);
    }

    [Fact]
    public void DetailLine_SeverityIgnored_WhenTypeDoesNotRequireIt()
    {
        // Symptom doesn't require severity, so even if a severity value were passed,
        // DetailLine should not surface it — only Note.
        var line = EntryRules.DetailLine(BuiltInEntryTypes.Symptom, severity: Severity.Severe, pillName: null, note: "Headache");

        Assert.Equal("Headache", line);
    }

    [Fact]
    public void DetailLine_PillNameIgnored_WhenTypeDoesNotRequireIt()
    {
        var line = EntryRules.DetailLine(BuiltInEntryTypes.Symptom, severity: null, pillName: "Aspirin", note: "Headache");

        Assert.Equal("Headache", line);
    }

    [Fact]
    public void DetailLine_CustomType_ShowsNoteOnly_IgnoringBuiltInFields()
    {
        var line = EntryRules.DetailLine(CustomType, severity: Severity.Severe, pillName: "Aspirin", note: "128/82");

        Assert.Equal("128/82", line);
    }

    [Fact]
    public void DetailLine_CustomType_WithoutNote_ReturnsNull()
    {
        var line = EntryRules.DetailLine(CustomType, severity: null, pillName: null, note: null);

        Assert.Null(line);
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

    // ---- OrderEntries ----

    private sealed record TestEntry(DateTimeOffset OccurredAt, string Type, string Label);

    private static IReadOnlyList<string> OrderLabels(IEnumerable<TestEntry> entries) =>
        EntryRules.OrderEntries(entries, e => e.OccurredAt, e => e.Type)
            .Select(e => e.Label)
            .ToList();

    [Fact]
    public void OrderEntries_EqualTimestamps_OrdersByTypeNameAlphabetically()
    {
        // The built-ins are entered in their seed order (Symptom, Bleeding, Pill, Cough,
        // Meal), which is not alphabetical; sharing one timestamp, they must come back
        // alphabetical: Bleeding, Cough, Meal, Pill, Symptom.
        var same = new DateTimeOffset(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);
        var entries = new[]
        {
            new TestEntry(same, BuiltInEntryTypes.Symptom, "symptom"),
            new TestEntry(same, BuiltInEntryTypes.Bleeding, "bleeding"),
            new TestEntry(same, BuiltInEntryTypes.Pill, "pill"),
            new TestEntry(same, BuiltInEntryTypes.Cough, "cough"),
            new TestEntry(same, BuiltInEntryTypes.Meal, "meal"),
        };

        var ordered = OrderLabels(entries);

        Assert.Equal(new[] { "bleeding", "cough", "meal", "pill", "symptom" }, ordered);
    }

    [Fact]
    public void OrderEntries_EqualTimestamps_CustomTypesSortAmongBuiltInsByName()
    {
        // The tie-break is the plain name, so a custom type takes its alphabetical place
        // rather than being pushed to either end.
        var same = new DateTimeOffset(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);
        var entries = new[]
        {
            new TestEntry(same, BuiltInEntryTypes.Pill, "pill"),
            new TestEntry(same, "Mood", "mood"),
            new TestEntry(same, BuiltInEntryTypes.Bleeding, "bleeding"),
        };

        var ordered = OrderLabels(entries);

        Assert.Equal(new[] { "bleeding", "mood", "pill" }, ordered);
    }

    [Fact]
    public void OrderEntries_DifferingTimestamps_OrdersChronologically_RegardlessOfType()
    {
        var t0 = new DateTimeOffset(2026, 8, 15, 8, 0, 0, TimeSpan.Zero);
        var t1 = new DateTimeOffset(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);

        // Type order deliberately works against alphabetical (Symptom sorts last
        // alphabetically but is entered first here, at the earliest time) to prove
        // the timestamp — not the type — drives the order when they differ.
        var entries = new[]
        {
            new TestEntry(t1, BuiltInEntryTypes.Bleeding, "second"),
            new TestEntry(t0, BuiltInEntryTypes.Symptom, "first"),
            new TestEntry(t2, BuiltInEntryTypes.Pill, "third"),
        };

        var ordered = OrderLabels(entries);

        Assert.Equal(new[] { "first", "second", "third" }, ordered);
    }

    [Fact]
    public void OrderEntries_MixedTimestamps_ChronologicalPrimary_AlphabeticalTieBreak()
    {
        var t0 = new DateTimeOffset(2026, 8, 15, 8, 0, 0, TimeSpan.Zero);
        var t1 = new DateTimeOffset(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);

        // Two entries tie at t1 (Pill vs Cough); one entry sits alone at the earlier t0.
        var entries = new[]
        {
            new TestEntry(t1, BuiltInEntryTypes.Pill, "t1-pill"),
            new TestEntry(t0, BuiltInEntryTypes.Meal, "t0-meal"),
            new TestEntry(t1, BuiltInEntryTypes.Cough, "t1-cough"),
        };

        var ordered = OrderLabels(entries);

        Assert.Equal(new[] { "t0-meal", "t1-cough", "t1-pill" }, ordered);
    }
}
