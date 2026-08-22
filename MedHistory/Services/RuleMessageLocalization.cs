using Microsoft.Extensions.Localization;

namespace MedHistory.Services;

/// <summary>
/// The one place a rule's verdict turns into copy. It lives here rather than in each controller
/// because all four that validate anything need the same two lines, and because a message that
/// reaches <c>ModelState</c> has no second chance: <c>asp-validation-summary</c> renders whatever
/// string it finds there verbatim, so the lookup has to happen before the message is added, not in
/// the view the way <c>_Weight.cshtml</c> can afford to do with its one hole-free message.
/// </summary>
public static class RuleMessageLocalization
{
    /// <summary>
    /// The message in the reader's language. An untranslated key comes back as itself, which is
    /// already the English sentence — the whole reason keys are the source text.
    /// </summary>
    public static string Localize(this IStringLocalizer localizer, RuleMessage message) =>
        message.Args.Length == 0
            ? localizer[message.Key]
            // Deliberately not Localize(key) + string.Format: the localizer applies the format to
            // the *translated* string, which is the only version whose holes are in Thai order.
            : localizer[message.Key, message.Args];
}
