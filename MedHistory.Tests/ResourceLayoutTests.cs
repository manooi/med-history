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

    /// <summary>Any month; the reports below are only asked what key they name, not what is in them.</summary>
    private static readonly DateOnly August = new(2026, 8, 1);

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
    private const string HubBaseName = "MedHistory.Resources.Views.Reports.Index";
    private const string MedReportBaseName = "MedHistory.Resources.Views.Report.Index";
    private const string TypeReportBaseName = "MedHistory.Resources.Views.TypeReport.Index";
    private const string AnxietyReportBaseName = "MedHistory.Resources.Views.AnxietyReport.Index";
    private const string WeightReportBaseName = "MedHistory.Resources.Views.WeightReport.Index";
    private const string DoctorReportBaseName = "MedHistory.Resources.Views.DoctorReport.Index";
    private const string HistoryBaseName = "MedHistory.Resources.Views.History.Index";
    private const string SearchBaseName = "MedHistory.Resources.Views.Search.Index";

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
    [InlineData("calm", "สงบ")]
    [InlineData("ok", "โอเค")]
    [InlineData("tense", "ตึงเครียด")]
    [InlineData("anxious", "กังวล")]
    [InlineData("panic", "ตื่นตระหนก")]
    [InlineData("kg", "กก.")]
    [InlineData("Back to today", "กลับไปหน้าวันนี้")]
    [InlineData("This month", "เดือนนี้")]
    [InlineData("Previous month", "เดือนก่อนหน้า")]
    [InlineData("Next month", "เดือนถัดไป")]
    [InlineData("Newer", "ใหม่กว่า")]
    [InlineData("Older", "เก่ากว่า")]
    [InlineData("Page {0} of {1}", "หน้า {0} จาก {1}")]
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
    [InlineData(HubBaseName, "Med report")]
    [InlineData(HubBaseName, "Anxiety report")]
    [InlineData(HubBaseName, "Weight report")]
    [InlineData(HubBaseName, "Entries for whichever types you pick, day by day")]
    [InlineData(HubBaseName, "Month calendar of med slots ticked vs planned")]
    [InlineData(HubBaseName, "Month calendar of the daily anxiety vote")]
    [InlineData(HubBaseName, "Month calendar of logged weight readings")]
    [InlineData(HubBaseName, "Printable date-range summary for visits")]
    [InlineData(MedReportBaseName, "Report — {0}")]
    [InlineData(MedReportBaseName, "nothing planned")]
    [InlineData(MedReportBaseName, "{0}/{1} doses")]
    [InlineData(MedReportBaseName, "{0} — nothing planned")]
    [InlineData(MedReportBaseName, "{0} — {1} of {2} doses")]
    [InlineData(MedReportBaseName, "every dose")]
    [InlineData(MedReportBaseName, "some doses")]
    [InlineData(MedReportBaseName, "none of them")]
    [InlineData(MedReportBaseName, "unboxed — nothing planned")]
    [InlineData(TypeReportBaseName, "Type report")]
    [InlineData(TypeReportBaseName, "Type report — {0}")]
    [InlineData(TypeReportBaseName, "Pick one or more types.")]
    [InlineData(TypeReportBaseName, "Clear")]
    [InlineData(TypeReportBaseName, "Newest first ↓")]
    [InlineData(TypeReportBaseName, "Oldest first ↑")]
    [InlineData(TypeReportBaseName, "Nothing logged for this type.")]
    [InlineData(TypeReportBaseName, "Nothing logged for these types.")]
    [InlineData(AnxietyReportBaseName, "Anxiety — {0}")]
    [InlineData(AnxietyReportBaseName, "{0} voted")]
    [InlineData(AnxietyReportBaseName, "{0} — no vote")]
    [InlineData(WeightReportBaseName, "Weight — {0}")]
    [InlineData(WeightReportBaseName, "no readings")]
    [InlineData(WeightReportBaseName, "{0} day(s) measured")]
    [InlineData(WeightReportBaseName, "Min")]
    [InlineData(WeightReportBaseName, "Avg")]
    [InlineData(WeightReportBaseName, "Max")]
    [InlineData(WeightReportBaseName, "{0} — no reading")]
    [InlineData(WeightReportBaseName, "{0} — {1} kg")]
    [InlineData(DoctorReportBaseName, "Doctor report")]
    [InlineData(DoctorReportBaseName, "Printable date-range summary for visits")]
    [InlineData(DoctorReportBaseName, "From")]
    [InlineData(DoctorReportBaseName, "To")]
    [InlineData(DoctorReportBaseName, "Apply")]
    [InlineData(DoctorReportBaseName, "Last 30 days")]
    [InlineData(DoctorReportBaseName, "Last 90 days")]
    [InlineData(DoctorReportBaseName, "med-history — {0} to {1}")]
    [InlineData(DoctorReportBaseName, "Nothing logged in this range.")]
    [InlineData(DoctorReportBaseName, "anxiety voted {0}/{1} days")]
    [InlineData(DoctorReportBaseName, "anxiety: {0}")]
    [InlineData(DoctorReportBaseName, "({0} photo)")]
    [InlineData(DoctorReportBaseName, "({0} photos)")]
    [InlineData(HistoryBaseName, "No entries yet.")]
    [InlineData(SearchBaseName, "Search — {0}")]
    [InlineData(SearchBaseName, "Notes and med names")]
    [InlineData(SearchBaseName, "Query")]
    [InlineData(SearchBaseName, "No entries matched “{0}”.")]
    [InlineData(SearchBaseName, "1 day matched")]
    [InlineData(SearchBaseName, "{0} days matched")]
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

    [Theory]
    // The report copy that carries numbers. Same failure as above and quieter: a fraction whose
    // translation dropped a hole reads as a report with no counts in it, not as an error.
    [InlineData(MedReportBaseName, "{0}/{1} doses", 2)]
    [InlineData(MedReportBaseName, "{0} — nothing planned", 1)]
    [InlineData(MedReportBaseName, "{0} — {1} of {2} doses", 3)]
    [InlineData(AnxietyReportBaseName, "{0} voted", 1)]
    [InlineData(AnxietyReportBaseName, "{0} — no vote", 1)]
    [InlineData(WeightReportBaseName, "{0} day(s) measured", 1)]
    [InlineData(WeightReportBaseName, "{0} — no reading", 1)]
    [InlineData(WeightReportBaseName, "{0} — {1} kg", 2)]
    [InlineData(DoctorReportBaseName, "med-history — {0} to {1}", 2)]
    [InlineData(DoctorReportBaseName, "anxiety voted {0}/{1} days", 2)]
    [InlineData(DoctorReportBaseName, "anxiety: {0}", 1)]
    [InlineData(DoctorReportBaseName, "({0} photo)", 1)]
    [InlineData(DoctorReportBaseName, "({0} photos)", 1)]
    [InlineData(SearchBaseName, "Search — {0}", 1)]
    [InlineData(SearchBaseName, "No entries matched “{0}”.", 1)]
    [InlineData(SearchBaseName, "{0} days matched", 1)]
    [InlineData(SharedBaseName, "Page {0} of {1}", 2)]
    public void EveryReportPlaceholderSurvivesTranslation(string baseName, string key, int holes)
    {
        var thai = Read(baseName, key);

        for (var hole = 0; hole < holes; hole++)
        {
            Assert.Contains($"{{{hole}}}", thai);
        }
    }

    [Fact]
    public void TheProgressKeysTheReportsReturnAreRealKeysInTheirOwnViewFiles()
    {
        // ReportMonth and AnxietyMonth stay pure by naming a resource instead of holding copy, the
        // way CultureRules.LanguageName does. Nothing but this connects the rules to the .resx, so
        // a rename on either side would otherwise surface as raw English under the month name.
        Assert.Equal("ไม่มีแผน", Read(MedReportBaseName, ReportRules.BuildMonth(August, [], []).ProgressKey));

        var planned = ReportRules.BuildMonth(
            August,
            [new ReportAllocation(1, new DateOnly(2026, 8, 12), MedSlots.Morning)],
            []);
        Assert.Equal("{0}/{1} ขนาดยา", Read(MedReportBaseName, planned.ProgressKey));

        Assert.Equal(
            "เลือกระดับแล้ว {0} วัน",
            Read(AnxietyReportBaseName, AnxietyRules.BuildMonth(August, []).ProgressKey));
    }

    [Theory]
    [InlineData(TypeReportSort.NewestFirst)]
    [InlineData(TypeReportSort.OldestFirst)]
    public void TheSortLabelsTypeReportRulesReturnsAreRealKeysInTheTypeReportFile(TypeReportSort sort)
    {
        // Same contract: the toggle's copy is a key the view looks up, arrows and all.
        var thai = Read(TypeReportBaseName, TypeReportRules.SortLabel(sort));

        Assert.False(string.IsNullOrWhiteSpace(thai));
        Assert.NotEqual(TypeReportRules.SortLabel(sort), thai);
    }

    [Theory]
    [InlineData(AnxietyLevel.Calm, "สงบ")]
    [InlineData(AnxietyLevel.Ok, "โอเค")]
    [InlineData(AnxietyLevel.Tense, "ตึงเครียด")]
    [InlineData(AnxietyLevel.Anxious, "กังวล")]
    [InlineData(AnxietyLevel.Panic, "ตื่นตระหนก")]
    public void TheLevelNamesAnxietyRulesReturnsAreSharedKeys(AnxietyLevel level, string thai)
    {
        // The day widget, the anxiety report's grid and legend, and the doctor report all render
        // AnxietyRules.Label — one file answers all four, which is why it is the shared one.
        Assert.Equal(thai, Read(SharedBaseName, AnxietyRules.Label(level)));
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
