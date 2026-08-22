namespace MedHistory.Services;

/// <summary>
/// Pure culture rules — no HTTP, no ambient <c>CultureInfo</c>. The app speaks two languages and
/// the reader picks one with a single button, so every question the toggle asks is answered here:
/// which cultures exist, whether a name is one of them, what a click switches to, and what the
/// button says. The controller and the layout stay dumb readers of these answers.
///
/// Names are the culture identifiers the framework knows (<c>en-US</c>, <c>th-TH</c>), not
/// display text — they travel in the culture cookie and into
/// <c>RequestLocalizationOptions</c>, so they are matched case-insensitively but never
/// reformatted, lower-cased or otherwise normalised here.
/// </summary>
public static class CultureRules
{
    /// <summary>The language the app renders in when nothing says otherwise.</summary>
    public const string Default = "en-US";

    /// <summary>The other one. Named because both the toggle and its fallback point at it.</summary>
    public const string Thai = "th-TH";

    /// <summary>
    /// Every culture the app supports, default first — the list <c>Program.cs</c> hands to
    /// <c>RequestLocalizationOptions</c> as both the supported cultures and the supported UI
    /// cultures, so a name that is not in here is a name the middleware will refuse.
    /// </summary>
    public static readonly IReadOnlyList<string> Supported = [Default, Thai];

    /// <summary>
    /// Whether <paramref name="name"/> is one of <see cref="Supported"/>. Case-insensitive
    /// because the value arrives as posted form input; absent or blank is simply not supported.
    /// </summary>
    public static bool IsSupported(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && Supported.Any(supported => string.Equals(supported, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The culture a click on the toggle moves to. An unknown or missing <paramref name="current"/>
    /// resolves to <see cref="Thai"/> rather than to the default: an unreadable cookie already
    /// renders the page in the default language, so offering to switch to the default would be a
    /// button that visibly does nothing. Pointing the broken case at the non-default language keeps
    /// the toggle useful — one click still gets the reader to the other language.
    /// </summary>
    public static string Toggle(string? current) =>
        string.Equals(current, Thai, StringComparison.OrdinalIgnoreCase) ? Default : Thai;

    /// <summary>
    /// The two-letter label on the button — <c>EN</c> or <c>TH</c>. Anything that is not Thai reads
    /// as <c>EN</c>, which is what an unresolvable culture renders as anyway.
    /// </summary>
    public static string ShortLabel(string name) =>
        string.Equals(name, Thai, StringComparison.OrdinalIgnoreCase) ? "TH" : "EN";

    /// <summary>
    /// The language's name, for the toggle's <c>aria-label</c> — a two-letter code spelled out
    /// letter by letter tells a screen reader nothing about what the button does.
    ///
    /// The returned value is a <em>resource key</em>, which under the app's convention is also the
    /// English text: the layout looks it up in <c>_Layout.&lt;culture&gt;.resx</c> and gets
    /// "ภาษาไทย" under Thai, or this string itself under English. Localizing here instead would
    /// mean handing this class an <c>IStringLocalizer</c> and an ambient culture, and it stays pure.
    /// </summary>
    public static string LanguageName(string name) =>
        string.Equals(name, Thai, StringComparison.OrdinalIgnoreCase) ? "Thai" : "English";
}
