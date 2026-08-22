using System.Globalization;
using MedHistory;
using MedHistory.Services;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedHistory.Tests;

/// <summary>
/// The type that carries a pure rule's verdict from the rule to the localizer, and the one
/// extension that spends it.
///
/// The failure this guards against is quiet: <c>string.Format</c> is perfectly happy to be handed
/// an argument no hole asks for, and a translation that dropped a hole would print a sentence with
/// the medication name simply missing from it. So the tests that matter here are the ones that
/// put the holes somewhere other than where English has them and check the values still land.
/// </summary>
public class RuleMessageTests
{
    // Two keys that really are in Resources/SharedResource.th.resx, so this exercises the same
    // path a controller does rather than a stub that could agree with a broken lookup.
    private const string NoHoles = "Dose is required.";
    private const string OneHole = "Severity is required for {0} entries.";
    private const string TwoHoles = "Dose must be between {0} and {1}.";

    [Fact]
    public void AKeyWithNoValuesIsJustItsKey()
    {
        RuleMessage message = NoHoles;

        // The implicit conversion is what lets a rule with nothing to fill in go on returning a
        // plain literal, which most of them do.
        Assert.Equal(NoHoles, message.Key);
        Assert.Empty(message.Args);
        Assert.Equal(NoHoles, message.Text);
    }

    [Fact]
    public void TheValuesFillTheHolesInOrder()
    {
        var message = new RuleMessage(TwoHoles, "0.25", "99");

        Assert.Equal(TwoHoles, message.Key);
        Assert.Equal("Dose must be between 0.25 and 99.", message.Text);
        Assert.Equal(message.Text, message.ToString());
    }

    [Fact]
    public void AnUntranslatedKeyComesBackAsTheEnglishSourceText()
    {
        // The whole reason keys are the English sentence: nothing has to exist for English to
        // read correctly, in either language's request.
        using var scope = new CultureScope("th-TH");

        Assert.Equal(
            "Nothing has ever translated this.",
            Localizer().Localize("Nothing has ever translated this."));
    }

    [Fact]
    public void AnUntranslatedKeyStillFillsItsHoles()
    {
        using var scope = new CultureScope("th-TH");

        var message = new RuleMessage("No translation for {0} yet.", "Pill A");

        Assert.Equal("No translation for Pill A yet.", Localizer().Localize(message));
    }

    [Fact]
    public void AHoleFreeKeyIsLookedUpUnderTheReadersCulture()
    {
        var localizer = Localizer();

        using (new CultureScope("th-TH"))
        {
            Assert.Equal("ต้องระบุขนาดยา", localizer.Localize(NoHoles));
        }

        using (new CultureScope("en-US"))
        {
            Assert.Equal(NoHoles, localizer.Localize(NoHoles));
        }
    }

    [Fact]
    public void TheHolesAreFilledAfterTranslation_NotBefore()
    {
        // The one that matters. Thai puts the entry type at the end of a sentence that opens with
        // the verb, so the {0} is nowhere near where English has it. Formatting the English first
        // and translating afterwards would have nothing left to translate; the localizer applies
        // string.Format to the Thai, which is why the value lands in the Thai position.
        using var scope = new CultureScope("th-TH");

        var thai = Localizer().Localize(new RuleMessage(OneHole, "Bleeding"));

        Assert.Equal("ต้องระบุความรุนแรงสำหรับบันทึกประเภท Bleeding", thai);
        Assert.DoesNotContain("{0}", thai);

        // English opens with the value's neighbour instead — same message, different position.
        Assert.StartsWith("ต้องระบุ", thai);
    }

    [Fact]
    public void EveryValueSurvivesAReorderedTranslation()
    {
        // Two holes, and Thai does not necessarily keep them adjacent. Both have to arrive.
        using var scope = new CultureScope("th-TH");

        var thai = Localizer().Localize(new RuleMessage(TwoHoles, "0.25", "99"));

        Assert.Contains("0.25", thai);
        Assert.Contains("99", thai);
        Assert.False(thai.Contains('{'), $"An unfilled hole is left in '{thai}'.");
    }

    [Fact]
    public void TheEnglishTextIsFormattedInvariantly()
    {
        // Text is the fallback for a caller with no localizer, so it must not pick up whatever
        // culture the thread happens to be carrying.
        using var scope = new CultureScope("th-TH");

        var message = new RuleMessage("Range too long (max {0} days).", 366);

        Assert.Equal("Range too long (max 366 days).", message.Text);
        Assert.Equal(
            string.Format(CultureInfo.InvariantCulture, "Range too long (max {0} days).", 366),
            message.Text);
    }

    /// <summary>
    /// Wired the way <c>Program.cs</c> wires it — the real factory, the real
    /// <c>ResourcesPath</c>, the real marker type — so a resource file in the wrong place fails
    /// here rather than falling silently back to English on the page.
    /// </summary>
    private static IStringLocalizer<SharedResource> Localizer() =>
        new StringLocalizer<SharedResource>(
            new ResourceManagerStringLocalizerFactory(
                Options.Create(new LocalizationOptions { ResourcesPath = "Resources" }),
                NullLoggerFactory.Instance));
}
