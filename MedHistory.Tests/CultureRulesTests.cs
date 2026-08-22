using MedHistory.Services;

namespace MedHistory.Tests;

public class CultureRulesTests
{
    // ---- Supported ----

    [Fact]
    public void Supported_IsTheTwoLanguages_DefaultFirst()
    {
        // Program.cs feeds this list to RequestLocalizationOptions verbatim, and the first entry
        // is the language a reader with no cookie gets.
        Assert.Equal(new[] { "en-US", "th-TH" }, CultureRules.Supported);
        Assert.Equal(CultureRules.Default, CultureRules.Supported[0]);
    }

    // ---- IsSupported ----

    [Theory]
    [InlineData("en-US")]
    [InlineData("th-TH")]
    public void IsSupported_KnownName_IsTrue(string name)
    {
        Assert.True(CultureRules.IsSupported(name));
    }

    [Theory]
    [InlineData("EN-US")]
    [InlineData("en-us")]
    [InlineData("TH-th")]
    public void IsSupported_IsCaseInsensitive(string name)
    {
        // The name arrives as posted form input, so its casing is not ours to rely on.
        Assert.True(CultureRules.IsSupported(name));
    }

    [Fact]
    public void IsSupported_Null_IsFalse()
    {
        Assert.False(CultureRules.IsSupported(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void IsSupported_Blank_IsFalse(string name)
    {
        Assert.False(CultureRules.IsSupported(name));
    }

    [Theory]
    [InlineData("th")]
    [InlineData("en")]
    [InlineData("th-TH-x-lvariant")]
    [InlineData("fr-FR")]
    [InlineData("not a culture")]
    public void IsSupported_UnknownOrPartialName_IsFalse(string name)
    {
        // Only the exact identifiers the middleware was configured with count — a bare language
        // tag is not one of them.
        Assert.False(CultureRules.IsSupported(name));
    }

    // ---- Toggle ----

    [Fact]
    public void Toggle_English_IsThai()
    {
        Assert.Equal("th-TH", CultureRules.Toggle("en-US"));
    }

    [Fact]
    public void Toggle_Thai_IsEnglish()
    {
        Assert.Equal("en-US", CultureRules.Toggle("th-TH"));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("th-TH")]
    public void Toggle_Twice_IsWhereItStarted(string start)
    {
        Assert.Equal(start, CultureRules.Toggle(CultureRules.Toggle(start)));
    }

    [Theory]
    [InlineData("EN-us")]
    [InlineData("TH-th")]
    public void Toggle_IsCaseInsensitive(string current)
    {
        Assert.Equal(
            CultureRules.Toggle(current.ToLowerInvariant()),
            CultureRules.Toggle(current));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("fr-FR")]
    [InlineData("garbage")]
    public void Toggle_UnknownOrMissingCurrent_IsThai(string? current)
    {
        // A broken or absent cookie already renders in the default language, so offering to switch
        // to the default would be a button that visibly does nothing. Pointing at the non-default
        // language keeps one click useful.
        Assert.Equal("th-TH", CultureRules.Toggle(current));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("fr-FR")]
    public void Toggle_AlwaysLandsOnASupportedCulture(string? current)
    {
        Assert.Contains(CultureRules.Toggle(current), CultureRules.Supported);
    }

    // ---- ShortLabel ----

    [Fact]
    public void ShortLabel_English_IsEn()
    {
        Assert.Equal("EN", CultureRules.ShortLabel("en-US"));
    }

    [Fact]
    public void ShortLabel_Thai_IsTh()
    {
        Assert.Equal("TH", CultureRules.ShortLabel("th-TH"));
    }

    [Theory]
    [InlineData("TH-th")]
    [InlineData("th-th")]
    public void ShortLabel_IsCaseInsensitive(string name)
    {
        Assert.Equal("TH", CultureRules.ShortLabel(name));
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("")]
    public void ShortLabel_UnknownName_ReadsAsTheDefault(string name)
    {
        Assert.Equal("EN", CultureRules.ShortLabel(name));
    }

    // ---- LanguageName ----

    [Fact]
    public void LanguageName_SpellsOutTheLanguage()
    {
        // What the toggle's aria-label says; the two-letter label alone tells a screen reader
        // nothing about what the button does.
        Assert.Equal("English", CultureRules.LanguageName("en-US"));
        Assert.Equal("Thai", CultureRules.LanguageName("th-TH"));
    }

    // ---- the toggle as the layout uses it ----

    [Theory]
    [InlineData("en-US", "th-TH", "TH")]
    [InlineData("th-TH", "en-US", "EN")]
    public void ButtonSaysTheLanguageItSwitchesTo(string current, string expectedNext, string expectedLabel)
    {
        var next = CultureRules.Toggle(current);

        Assert.Equal(expectedNext, next);
        Assert.Equal(expectedLabel, CultureRules.ShortLabel(next));
        Assert.NotEqual(CultureRules.ShortLabel(current), CultureRules.ShortLabel(next));
    }
}
