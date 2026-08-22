using System.Globalization;
using System.Resources;
using MedHistory;
using MedHistory.Models;
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
    private const string MedsIndexBaseName = "MedHistory.Resources.Views.Meds.Index";
    private const string MedsEditBaseName = "MedHistory.Resources.Views.Meds.Edit";
    private const string TypesIndexBaseName = "MedHistory.Resources.Views.Types.Index";

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
    [InlineData("Doctor", "แพทย์")]
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
    [InlineData(MedsIndexBaseName, "Meds")]
    [InlineData(MedsIndexBaseName, "Stock")]
    [InlineData(MedsIndexBaseName, "This day's plan")]
    // The slot, meal and method words are keyed on what MedPlanRules returns, so a rename there
    // would leave the screen reading English with nothing else to notice.
    [InlineData(MedsIndexBaseName, "morning")]
    [InlineData(MedsIndexBaseName, "after meal")]
    [InlineData(MedsIndexBaseName, "eyedrop")]
    [InlineData(MedsEditBaseName, "Edit medication")]
    [InlineData(MedsEditBaseName, "bedtime")]
    [InlineData(MedsEditBaseName, "any time")]
    [InlineData(TypesIndexBaseName, "New type")]
    [InlineData(TypesIndexBaseName, "Built-in")]
    [InlineData(TypesIndexBaseName, "Deactivate")]
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

        // The meds screens carry the count, the stock's medication name and the page's day the
        // same way — Thai moves each of them, so a dropped hole loses the value outright.
        Assert.Contains("{0}", Read(MedsIndexBaseName, "{0} taken"));
        Assert.Contains("{0}", Read(MedsIndexBaseName, "{0} left"));
        Assert.Contains("{0}", Read(MedsIndexBaseName, "Total stocked of {0}"));
        Assert.Contains("{0}", Read(MedsEditBaseName,
            "Only allocations dated on or after {0} that still carry this medication's current name are changed. " +
            "Days before this one, and doses already logged, are never touched."));
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

    [Fact]
    public void EveryWordTheMedScreensTakeFromMedPlanRulesIsTranslated()
    {
        // The med screens look these up by the English label MedPlanRules returns, because that
        // same label is what a tick writes into an entry's stored note — translating the source
        // would rewrite what goes into the database. So the key set is not a list anyone
        // maintains by hand: it is whatever the rules produce, which is what this walks. A word
        // renamed there goes silently untranslated on screen; here it fails.
        var words = MedPlanRules.AllSlots.Select(MedPlanRules.SlotLabel)
            .Concat(Enum.GetValues<MealRelation>().Select(MedPlanRules.MealRelationOption))
            .Concat(Enum.GetValues<MedMethod>().Select(MedPlanRules.MethodOption))
            .Where(word => word.Length > 0);

        var missing = words
            .SelectMany(word => new[] { MedsIndexBaseName, MedsEditBaseName }
                .Where(baseName => Read(baseName, word) is null or "")
                .Select(baseName => $"{baseName}: '{word}'"))
            .ToList();

        Assert.True(missing.Count == 0,
            "MedPlanRules words with no Thai on the meds screens:" + Environment.NewLine +
            string.Join(Environment.NewLine, missing));
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
