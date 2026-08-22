using System.Globalization;
using System.Resources;
using MedHistory;
using MedHistory.Services;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedHistory.Tests;

/// <summary>
/// The .resx convention has exactly one silent failure mode: a file in the wrong place still
/// compiles, still ships, and every lookup quietly falls back to the key — i.e. to English. Nothing
/// else in the build notices. These tests do the same lookup the localizer does, through the same
/// base names, so a moved or misnamed file fails here instead of on the page.
/// </summary>
public class ResourceLayoutTests
{
    private static readonly CultureInfo Thai = new("th-TH");

    // What ResourceManagerStringLocalizerFactory computes: root namespace + the configured
    // ResourcesPath + the type's name with the root namespace trimmed off.
    private const string SharedBaseName = "MedHistory.Resources.SharedResource";

    // What IViewLocalizer computes from a view's path: root namespace + ResourcesPath + the path
    // with its slashes turned into dots. Resources/Views/<Controller>/<View>.th.resx.
    private const string LayoutBaseName = "MedHistory.Resources.Views.Shared._Layout";
    private const string LoginBaseName = "MedHistory.Resources.Views.Account.Login";
    private const string ErrorBaseName = "MedHistory.Resources.Views.Shared.Error";

    [Fact]
    public void TheRealFactoryResolvesTheSharedFileFromTheMarkerType()
    {
        // Not the hand-computed base name above but the framework's own derivation, wired the way
        // Program.cs wires it. This is what proves the marker type's location and ResourcesPath
        // actually agree with where the .resx sits.
        var factory = new ResourceManagerStringLocalizerFactory(
            Options.Create(new LocalizationOptions { ResourcesPath = "Resources" }),
            NullLoggerFactory.Instance);

        var localizer = factory.Create(typeof(SharedResource));

        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = Thai;
            Assert.Equal("บันทึก", localizer["Save"]);
            // The whole point of keying on English: an absent key is not an error, it is the key.
            Assert.Equal("Not translated yet", localizer["Not translated yet"]);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void SharedResourceMarkerLivesWhereItsBaseNameSaysItDoes()
    {
        // The marker's full name is the whole input to the base name above; a stray namespace or a
        // move into a folder would repoint every shared string at a file that does not exist.
        Assert.Equal("MedHistory.SharedResource", typeof(SharedResource).FullName);
        Assert.Equal("MedHistory", typeof(SharedResource).Assembly.GetName().Name);
    }

    [Theory]
    [InlineData("Save", "บันทึก")]
    [InlineData("Cancel", "ยกเลิก")]
    [InlineData("Delete", "ลบ")]
    [InlineData("Remove", "นำออก")]
    [InlineData("Edit", "แก้ไข")]
    [InlineData("Add", "เพิ่ม")]
    [InlineData("Back", "ย้อนกลับ")]
    [InlineData("Today", "วันนี้")]
    [InlineData("History", "ประวัติ")]
    [InlineData("Reports", "รายงาน")]
    [InlineData("Search", "ค้นหา")]
    [InlineData("Types", "ประเภท")]
    [InlineData("Logout", "ออกจากระบบ")]
    [InlineData("By type", "ตามประเภท")]
    [InlineData("Med", "ยา")]
    [InlineData("Anxiety", "ความกังวล")]
    [InlineData("Weight", "น้ำหนัก")]
    [InlineData("Doctor", "หมอ")]
    public void SharedVocabularyIsTranslated(string key, string thai)
    {
        Assert.Equal(thai, Read(SharedBaseName, key));
    }

    [Theory]
    [InlineData(LayoutBaseName, "Menu")]
    [InlineData(LayoutBaseName, "Switch to {0}")]
    [InlineData(LayoutBaseName, "English")]
    [InlineData(LayoutBaseName, "Thai")]
    [InlineData(LayoutBaseName, "Theme: {0}. Switch to {1}.")]
    [InlineData(LayoutBaseName, "auto")]
    [InlineData(LayoutBaseName, "dark")]
    [InlineData(LayoutBaseName, "light")]
    [InlineData(LoginBaseName, "Login")]
    [InlineData(LoginBaseName, "Enter Passcode")]
    [InlineData(LoginBaseName, "{0} of {1} digits entered")]
    [InlineData(LoginBaseName, "Passcode")]
    [InlineData(LoginBaseName, "Sign in")]
    [InlineData(ErrorBaseName, "Error")]
    [InlineData(ErrorBaseName, "Something went wrong")]
    [InlineData(ErrorBaseName, "An error occurred while processing your request.")]
    [InlineData(ErrorBaseName, "Request ID")]
    [InlineData(ErrorBaseName, "Back to today")]
    public void PerViewKeyResolvesToThai(string baseName, string key)
    {
        var value = Read(baseName, key);

        Assert.False(string.IsNullOrWhiteSpace(value), $"{baseName}: '{key}' is missing or blank.");
        Assert.NotEqual(key, value);
    }

    [Fact]
    public void ThePlaceholdersSurviveTranslation()
    {
        // These three are handed to string.Format or to a .replace() in the browser, so a
        // translation that drops or renames a placeholder loses the number it was carrying.
        Assert.Contains("{0}", Read(LayoutBaseName, "Switch to {0}"));

        var theme = Read(LayoutBaseName, "Theme: {0}. Switch to {1}.");
        Assert.Contains("{0}", theme);
        Assert.Contains("{1}", theme);

        var digits = Read(LoginBaseName, "{0} of {1} digits entered");
        Assert.Contains("{0}", digits);
        Assert.Contains("{1}", digits);
    }

    [Fact]
    public void AnUnknownKeyReadsAsNothing_WhichIsWhatLetsEnglishShipWithoutAResx()
    {
        // The localizer turns this null into the key itself. That is the whole reason there is no
        // en-US file: the key is the English source text. If this ever started throwing instead,
        // every untranslated string would become an error page.
        Assert.Null(Read(SharedBaseName, "no such key"));
        Assert.Null(Read(SharedBaseName, "Menu"));
    }

    [Fact]
    public void TheLanguageNamesCultureRulesReturnsAreRealKeysInTheLayoutFile()
    {
        // CultureRules stays pure by returning a resource key; nothing but this test connects the
        // two files, so a rename on either side would otherwise show up as raw English in the
        // toggle's aria-label.
        Assert.Equal("ภาษาไทย", Read(LayoutBaseName, CultureRules.LanguageName(CultureRules.Thai)));
        Assert.Equal("ภาษาอังกฤษ", Read(LayoutBaseName, CultureRules.LanguageName(CultureRules.Default)));
    }

    // Same call ResourceManagerStringLocalizer makes. Null means "not in the .th file", which the
    // localizer reports as "use the key".
    private static string? Read(string baseName, string key)
    {
        var manager = new ResourceManager(baseName, typeof(SharedResource).Assembly);
        try
        {
            return manager.GetString(key, Thai);
        }
        catch (MissingManifestResourceException)
        {
            // No neutral .resources exists by design, so a key absent from the Thai file lands
            // here rather than returning null.
            return null;
        }
    }
}
