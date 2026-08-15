using MedHistory.Models;
using MedHistory.Services;

namespace MedHistory.Tests;

public class EntryTypeRulesTests
{
    private static readonly (string Name, bool IsActive)[] Types =
    [
        (BuiltInEntryTypes.Symptom, true),
        (BuiltInEntryTypes.Med, true),
        ("Blood pressure", true),
        ("Physio", false),
    ];

    // ---- NormalizeName ----

    [Fact]
    public void NormalizeName_TrimsSurroundingWhitespace()
    {
        Assert.Equal("Blood pressure", EntryTypeRules.NormalizeName("  Blood pressure  "));
    }

    [Fact]
    public void NormalizeName_KeepsInnerSpacesAndCasing()
    {
        Assert.Equal("Blood Pressure", EntryTypeRules.NormalizeName("Blood Pressure"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n ")]
    public void NormalizeName_NothingLeftAfterTrimming_ReturnsNull(string? raw)
    {
        Assert.Null(EntryTypeRules.NormalizeName(raw));
    }

    // ---- NamesMatch ----

    [Theory]
    [InlineData("Cough", "cough")]
    [InlineData("cough", "COUGH")]
    [InlineData("Blood pressure", "blood PRESSURE")]
    public void NamesMatch_IgnoresCase(string a, string b)
    {
        Assert.True(EntryTypeRules.NamesMatch(a, b));
    }

    [Fact]
    public void NamesMatch_DifferentNames_False()
    {
        Assert.False(EntryTypeRules.NamesMatch("Cough", "Coughing"));
    }

    // ---- ValidateNewName ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateNewName_EmptyOrWhitespace_ReturnsRequiredError(string? raw)
    {
        var errors = EntryTypeRules.ValidateNewName(raw, []);

        Assert.Contains(errors, e => e.Contains("required"));
    }

    [Fact]
    public void ValidateNewName_UnusedName_NoErrors()
    {
        var errors = EntryTypeRules.ValidateNewName("Mood", ["Symptom", "Med"]);

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateNewName_ExactDuplicate_ReturnsError()
    {
        var errors = EntryTypeRules.ValidateNewName("Cough", ["Symptom", "Cough"]);

        Assert.Contains(errors, e => e.Contains("already exists"));
    }

    [Theory]
    [InlineData("cough")]
    [InlineData("COUGH")]
    [InlineData("CoUgH")]
    public void ValidateNewName_DuplicateInDifferentCase_ReturnsError(string raw)
    {
        var errors = EntryTypeRules.ValidateNewName(raw, ["Cough"]);

        Assert.Contains(errors, e => e.Contains("already exists"));
    }

    [Fact]
    public void ValidateNewName_DuplicateOnlyAfterTrimming_ReturnsError()
    {
        // The name is normalised before comparison, so padding cannot smuggle a duplicate in.
        var errors = EntryTypeRules.ValidateNewName("  cough  ", ["Cough"]);

        Assert.Contains(errors, e => e.Contains("already exists"));
    }

    [Fact]
    public void ValidateNewName_LongerThanMaxLength_ReturnsError()
    {
        var tooLong = new string('x', EntryTypeRules.NameMaxLength + 1);

        var errors = EntryTypeRules.ValidateNewName(tooLong, []);

        Assert.Contains(errors, e => e.Contains("characters or fewer"));
    }

    [Fact]
    public void ValidateNewName_ExactlyMaxLength_NoErrors()
    {
        var atLimit = new string('x', EntryTypeRules.NameMaxLength);

        var errors = EntryTypeRules.ValidateNewName(atLimit, []);

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateNewName_LengthMeasuredAfterTrimming()
    {
        // Padding that pushes the raw string past the limit is trimmed away first.
        var padded = "  " + new string('x', EntryTypeRules.NameMaxLength) + "  ";

        var errors = EntryTypeRules.ValidateNewName(padded, []);

        Assert.Empty(errors);
    }

    // ---- CheckAvailable ----

    [Theory]
    [InlineData(BuiltInEntryTypes.Symptom)]
    [InlineData(BuiltInEntryTypes.Med)]
    [InlineData("Blood pressure")]
    public void CheckAvailable_ActiveType_ReturnsOk(string name)
    {
        Assert.Equal(TypeAvailability.Ok, EntryTypeRules.CheckAvailable(name, Types));
    }

    [Fact]
    public void CheckAvailable_InactiveType_ReturnsInactive()
    {
        Assert.Equal(TypeAvailability.Inactive, EntryTypeRules.CheckAvailable("Physio", Types));
    }

    [Fact]
    public void CheckAvailable_UnknownType_ReturnsUnknown()
    {
        Assert.Equal(TypeAvailability.Unknown, EntryTypeRules.CheckAvailable("Nonsense", Types));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CheckAvailable_MissingName_ReturnsUnknown(string? name)
    {
        Assert.Equal(TypeAvailability.Unknown, EntryTypeRules.CheckAvailable(name, Types));
    }

    [Fact]
    public void CheckAvailable_MatchesCaseInsensitively_AndAfterTrimming()
    {
        Assert.Equal(TypeAvailability.Ok, EntryTypeRules.CheckAvailable("  mED ", Types));
    }

    [Fact]
    public void CheckAvailable_InactiveMatchedCaseInsensitively_StillInactive()
    {
        // Casing must never be a way around the inactive check.
        Assert.Equal(TypeAvailability.Inactive, EntryTypeRules.CheckAvailable("PHYSIO", Types));
    }

    [Fact]
    public void CheckAvailable_NoTypesAtAll_ReturnsUnknown()
    {
        Assert.Equal(TypeAvailability.Unknown, EntryTypeRules.CheckAvailable(BuiltInEntryTypes.Med, []));
    }

    // ---- SortForDisplay ----

    [Fact]
    public void SortForDisplay_BuiltInsKeepSeedOrder()
    {
        var shuffled = new[]
        {
            BuiltInEntryTypes.Meal,
            BuiltInEntryTypes.Note,
            BuiltInEntryTypes.Bleeding,
            BuiltInEntryTypes.Symptom,
            BuiltInEntryTypes.Cough,
            BuiltInEntryTypes.Med,
        };

        var sorted = EntryTypeRules.SortForDisplay(shuffled, name => name);

        Assert.Equal(BuiltInEntryTypes.All, sorted);
    }

    [Fact]
    public void BuiltInEntryTypes_All_EndsWithNote()
    {
        // Note was added after the original five; it must sort last so it doesn't
        // reshuffle the "+" button layout the user already knows.
        Assert.Equal(BuiltInEntryTypes.Note, BuiltInEntryTypes.All[^1]);
        Assert.Equal(
            new[]
            {
                BuiltInEntryTypes.Symptom,
                BuiltInEntryTypes.Bleeding,
                BuiltInEntryTypes.Med,
                BuiltInEntryTypes.Cough,
                BuiltInEntryTypes.Meal,
                BuiltInEntryTypes.Note,
            },
            BuiltInEntryTypes.All);
    }

    [Fact]
    public void SortForDisplay_CustomTypesFollowBuiltIns_Alphabetically()
    {
        var types = new[] { "Weight", BuiltInEntryTypes.Med, "Mood", BuiltInEntryTypes.Symptom, "Blood pressure" };

        var sorted = EntryTypeRules.SortForDisplay(types, name => name);

        Assert.Equal(
            new[] { BuiltInEntryTypes.Symptom, BuiltInEntryTypes.Med, "Blood pressure", "Mood", "Weight" },
            sorted);
    }

    [Fact]
    public void SortForDisplay_CustomTypes_SortedIgnoringCase()
    {
        var types = new[] { "zebra", "Apple", "banana" };

        var sorted = EntryTypeRules.SortForDisplay(types, name => name);

        Assert.Equal(new[] { "Apple", "banana", "zebra" }, sorted);
    }

    [Fact]
    public void SortForDisplay_SortsProjectedItems_NotJustStrings()
    {
        var rows = new[]
        {
            new EntryTypeRow { Id = 3, Name = "Mood", IsActive = true, IsBuiltIn = false },
            new EntryTypeRow { Id = 1, Name = BuiltInEntryTypes.Bleeding, IsActive = true, IsBuiltIn = true },
            new EntryTypeRow { Id = 2, Name = BuiltInEntryTypes.Symptom, IsActive = false, IsBuiltIn = true },
        };

        var sorted = EntryTypeRules.SortForDisplay(rows, r => r.Name);

        // Inactive built-ins keep their place in the list; only the day view filters them out.
        Assert.Equal(new[] { BuiltInEntryTypes.Symptom, BuiltInEntryTypes.Bleeding, "Mood" }, sorted.Select(r => r.Name));
    }

    [Fact]
    public void SortForDisplay_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(EntryTypeRules.SortForDisplay(Array.Empty<string>(), name => name));
    }
}
