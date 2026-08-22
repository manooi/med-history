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
/// The copy the server produces rather than a view: validation messages and the login screen's
/// errors. It has the same silent failure mode <see cref="ResourceLayoutTests"/> guards the views
/// against — a key with no entry falls back to English and nothing in the build notices — plus one
/// of its own, because these keys are not written in the file that renders them: a rule holds the
/// key, a controller looks it up, and neither end can see the other. A reworded rule therefore
/// leaves a correctly-placed .resx entry that nothing asks for any more.
///
/// So the key set below is not a list maintained by hand where it can be: the rules are driven
/// until they produce every message they have, and it is those keys that are looked up.
/// </summary>
public class ServerMessageResourceTests
{
    private static readonly CultureInfo Thai = new("th-TH");

    private const string SharedBaseName = "MedHistory.Resources.SharedResource";

    private static readonly DateOnly Day = new(2026, 8, 22);

    /// <summary>
    /// The messages no rule owns: the login screen's, the type table's race-loser, and the
    /// rename collision the meds edit form raises. These are written in a controller, so unlike
    /// the rules' own they cannot be walked — a rename here has to be a rename there too.
    /// </summary>
    [Theory]
    // AccountController, plus the [Required] on LoginViewModel.Password that Program.cs points
    // at this same file.
    [InlineData("Password is required.")]
    [InlineData("Password not configured.")]
    [InlineData("Incorrect password.")]
    [InlineData("Too many attempts — try again in {0} min.")]
    // TypesController, when two adds race past the rules and the unique index catches it.
    [InlineData("A type named \"{0}\" already exists.")]
    // MedsController, when an edit renames an allocation onto a day that already has that name.
    [InlineData("\"{0}\" is already used on {1}.")]
    [InlineData(ChecklistRules.MoreDaysKey)]
    public void AControllersOwnMessageIsTranslated(string key)
    {
        var thai = Read(key);

        Assert.False(string.IsNullOrWhiteSpace(thai), $"'{key}' is missing from the shared file.");
        Assert.NotEqual(key, thai);
    }

    [Fact]
    public void EveryMessageTheRulesProduceIsTranslated()
    {
        var keys = EveryRuleMessage()
            .Select(message => message.Key)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // A guard that walks an empty set passes forever and reads like cover, so the count is
        // pinned: every rule message this app has, as of this slice.
        // 3 from EntryTypeRules, 6 from EntryRules (ValidateOccurredAt included), 3 from
        // PhotoRules, 10 from ChecklistRules.
        Assert.Equal(22, keys.Count);

        var missing = keys.Where(key => Read(key) is null or "").ToList();

        Assert.True(missing.Count == 0,
            "Rule messages with no Thai in the shared file:" + Environment.NewLine +
            string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void EveryHoleSurvivesTranslation()
    {
        // string.Format ignores an argument no hole asks for, so a translation that dropped one
        // loses the name or the number it was carrying without failing anywhere. Thai moves these
        // holes around, which is the point — what has to hold is that each one is still there to
        // be filled, not where it sits.
        var dropped = EveryRuleMessage()
            .Concat(ControllerMessages())
            .Select(message => (message.Key, Thai: Read(message.Key), message.Args.Length))
            .Where(m => m.Thai is not null)
            .SelectMany(m => Enumerable.Range(0, m.Length)
                .Where(hole => !m.Thai!.Contains($"{{{hole}}}", StringComparison.Ordinal))
                .Select(hole => $"{{{hole}}} is missing from the Thai for '{m.Key}'"))
            .ToList();

        Assert.True(dropped.Count == 0, string.Join(Environment.NewLine, dropped));
    }

    [Fact]
    public void NoTranslationCarriesAHoleTheMessageCannotFill()
    {
        // The mirror image, and the louder failure: string.Format throws on a hole with no
        // argument behind it, so a Thai string that invented a {2} would turn a validation
        // message into an error page.
        var localizer = Localizer();

        using var scope = new CultureScope("th-TH");

        foreach (var message in EveryRuleMessage().Concat(ControllerMessages()))
        {
            var thai = localizer.Localize(message);

            Assert.False(thai.Contains('{'),
                $"'{message.Key}' left an unfilled hole: '{thai}'.");
        }
    }

    [Fact]
    public void OutsideThaiEveryMessageReadsAsItsOwnEnglishSourceText()
    {
        // Why there is no en-US file anywhere: the key is the English sentence, so a request in
        // the default culture gets correct copy from a lookup that found nothing.
        var localizer = Localizer();

        using var scope = new CultureScope("en-US");

        foreach (var message in EveryRuleMessage().Concat(ControllerMessages()))
        {
            Assert.Equal(message.Text, localizer.Localize(message));
        }
    }

    [Fact]
    public void TheDataAnnotationsMessageIsAKeyInTheSharedFile()
    {
        // Program.cs points DataAnnotations localization at SharedResource, so the ErrorMessage
        // on the model is a key in that file. Nothing else connects the attribute to the .resx —
        // a reworded ErrorMessage would just start rendering in English.
        var required = typeof(LoginViewModel)
            .GetProperty(nameof(LoginViewModel.Password))!
            .GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.RequiredAttribute), false)
            .Cast<System.ComponentModel.DataAnnotations.RequiredAttribute>()
            .Single();

        Assert.Equal("ต้องระบุรหัสผ่าน", Read(required.ErrorMessage!));
    }

    /// <summary>
    /// Every message the pure rules can produce, by driving each of them into every branch that
    /// has one. A rule that grows a message and is not driven here is the gap this cannot close
    /// by itself — but a rule that <em>rewords</em> one is caught, which is the failure that
    /// actually happens.
    /// </summary>
    private static IEnumerable<RuleMessage> EveryRuleMessage()
    {
        var tooLongType = new string('x', EntryTypeRules.NameMaxLength + 1);
        var tooLongMed = new string('x', ChecklistRules.NameMaxLength + 1);

        // EntryTypeRules
        foreach (var message in EntryTypeRules.ValidateNewName(null, [])) yield return message;
        foreach (var message in EntryTypeRules.ValidateNewName(tooLongType, [tooLongType])) yield return message;

        // EntryRules — every combination of type and field that breaks a rule.
        yield return EntryRules.ValidateOccurredAt(default)!;
        foreach (var message in EntryRules.Validate(BuiltInEntryTypes.Bleeding, null, null, null)) yield return message;
        foreach (var message in EntryRules.Validate(BuiltInEntryTypes.Med, Severity.Light, null, null)) yield return message;
        foreach (var message in EntryRules.Validate(BuiltInEntryTypes.Meal, null, "Aspirin", null)) yield return message;
        foreach (var message in EntryRules.Validate(BuiltInEntryTypes.Note, null, null, "   ")) yield return message;

        // PhotoRules
        foreach (var message in PhotoRules.Validate(null, 0)) yield return message;
        foreach (var message in PhotoRules.Validate("application/pdf", PhotoRules.MaxSizeBytes + 1)) yield return message;

        // ChecklistRules
        foreach (var message in ChecklistRules.ValidateNewAllocation(null, MedSlots.None, [])) yield return message;
        foreach (var message in ChecklistRules.ValidateNewAllocation(tooLongMed, MedSlots.None, [])) yield return message;
        foreach (var message in ChecklistRules.ValidateNewAllocation("Pill A", MedSlots.Morning, ["Pill A"])) yield return message;
        foreach (var message in ChecklistRules.ValidateDoseQuantity(null, out _)) yield return message;
        foreach (var message in ChecklistRules.ValidateDoseQuantity("nonsense", out _)) yield return message;
        foreach (var message in ChecklistRules.ValidateDoseQuantity("0", out _)) yield return message;
        foreach (var message in ChecklistRules.ValidateDoseQuantity("1.1", out _)) yield return message;
        foreach (var message in ChecklistRules.ValidateRange(Day, Day.AddDays(-1))) yield return message;
        foreach (var message in ChecklistRules.ValidateRange(Day, Day.AddDays(ChecklistRules.MaxRangeDays))) yield return message;
    }

    /// <summary>The controller-owned messages, shaped the same way so the hole checks can walk
    /// both sets at once.</summary>
    private static IEnumerable<RuleMessage> ControllerMessages()
    {
        yield return "Password is required.";
        yield return "Password not configured.";
        yield return "Incorrect password.";
        yield return new RuleMessage("Too many attempts — try again in {0} min.", 3);
        yield return new RuleMessage("A type named \"{0}\" already exists.", "Cough");
        yield return new RuleMessage("\"{0}\" is already used on {1}.", "Pill A", "22 Aug");
        yield return new RuleMessage(ChecklistRules.MoreDaysKey, "22 Aug", 2);
    }

    private static IStringLocalizer<SharedResource> Localizer() =>
        new StringLocalizer<SharedResource>(
            new ResourceManagerStringLocalizerFactory(
                Options.Create(new LocalizationOptions { ResourcesPath = "Resources" }),
                NullLoggerFactory.Instance));

    // The same call ResourceManagerStringLocalizer makes, against the same base name. Kept here
    // rather than shared with ResourceLayoutTests so neither file has to move for the other.
    private static string? Read(string key)
    {
        var manager = new ResourceManager(SharedBaseName, typeof(SharedResource).Assembly);
        try
        {
            return manager.GetString(key, Thai);
        }
        catch (MissingManifestResourceException)
        {
            // No neutral .resources exists by design, so an absent key lands here.
            return null;
        }
    }
}
