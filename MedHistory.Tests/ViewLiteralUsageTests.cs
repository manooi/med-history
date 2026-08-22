using System.Text;
using System.Text.RegularExpressions;

namespace MedHistory.Tests;

/// <summary>
/// The failure that let "Cancel", "Confirm" and the lightbox's "Close" survive a translation of
/// every screen around them: an untranslated string is not an error anywhere. It renders, it looks
/// right in English, and only a Thai reader ever sees it — and the shared partials, which no page
/// owns, are exactly where nobody thinks to look.
///
/// So this walks the views for the shape of the miss rather than for a list of known strings: copy
/// that reaches the reader without passing through a localizer. A run of English with no <c>@</c>
/// anywhere in it went through no lookup, by definition.
///
/// It is deliberately a net and not a proof. Razor is not parsed here, so the two checks below buy
/// their zero false positives by skipping anything that could be code — see <see cref="CodeIsh"/> —
/// which means a hardcoded label containing a quote or a semicolon would slip past. That is the
/// right trade for a guard: one that cried wolf on every calendar cell would be turned off, and a
/// guard that is off catches nothing at all.
/// </summary>
public class ViewLiteralUsageTests
{
    /// <summary>
    /// The attributes a reader or a screen reader is read out of. <c>value</c> is not among them:
    /// it is a posted value far more often than a label.
    /// </summary>
    private const string UserFacingAttributes = "aria-label|title|placeholder|alt|data-confirm";

    /// <summary>
    /// Characters that mean a run is far more likely to be C# caught between two tags — a
    /// statement inside an <c>@if</c> body, a class string, an interpolation — than a sentence
    /// anyone reads. Real copy in a view is a phrase; these are what code looks like.
    /// </summary>
    private static readonly char[] CodeIsh = [';', '{', '}', '=', '"'];

    /// <summary>
    /// The one word that is not copy: the product's own name, which is not translated in any
    /// language and is not a resource key.
    /// </summary>
    private static readonly string[] NotCopy = ["MedHistory"];

    [Fact]
    public void NoViewShowsEnglishThatWentThroughNoLocalizer()
    {
        var viewsDir = FindViewsDirectory();

        var offenders = Directory.EnumerateFiles(viewsDir, "*.cshtml", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .SelectMany(path => Offenders(Strip(File.ReadAllText(path)))
                .Select(offender => $"{Path.GetRelativePath(viewsDir, path)}: {offender}"))
            .ToList();

        Assert.True(offenders.Count == 0,
            "Views showing English that no localizer ever saw:" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders) + Environment.NewLine +
            "Body copy goes through @Localizer[\"...\"] (or @L for shared vocabulary); an " +
            "attribute takes @Localizer.GetString(\"...\"), which encodes. The key is the English " +
            "source text, so nothing else has to change for English to keep reading as it does.");
    }

    [Fact]
    public void TheGuardFindsTheShapeItIsLookingFor()
    {
        // A guard walking a clean tree passes forever and reads like cover, so it is shown the
        // three misses it was written for, exactly as they were written before this slice — and,
        // beside them, everything it must stay quiet about: a comment, a code block, a localized
        // attribute, a localized body string, a C# statement stranded between two tags inside an
        // @if body, an entity that is a glyph rather than a word, and the product's own name.
        const string view = """
            @* A comment saying Cancel is not copy. *@
            @{
                var classes = "border border-black";
            }
            <h1>MedHistory</h1>
            <dialog class="@classes">
                <button type="button">Cancel</button>
                <button type="button">
                    Confirm
                </button>
                <button aria-label="Close">&times;</button>
                <button aria-label="@Localizer.GetString("Close")">&larr;</button>
                <span>@Localizer["Save"]</span>
                @if (true)
                {
                    <span>@classes</span>
                    var label = "a stranded statement";
                    <span>@label</span>
                }
            </dialog>
            """;

        Assert.Equal(
            ["\"Cancel\"", "\"Confirm\"", "aria-label=\"Close\""],
            Offenders(Strip(view)).Order(StringComparer.Ordinal));
    }

    private static IEnumerable<string> Offenders(string view)
    {
        foreach (Match match in Regex.Matches(
                     view, $"\\b({UserFacingAttributes})\\s*=\\s*\"([^\"]*)\""))
        {
            var value = match.Groups[2].Value;
            if (!value.Contains('@') && HasWords(value))
            {
                yield return $"{match.Groups[1].Value}=\"{value}\"";
            }
        }

        // A text node: whatever sits between two tags. Razor expressions carry an @, which is the
        // whole test — a localized string cannot be written without one.
        foreach (Match match in Regex.Matches(view, ">([^<>]*)<"))
        {
            var text = match.Groups[1].Value;
            if (!text.Contains('@') && text.IndexOfAny(CodeIsh) < 0 && HasWords(text))
            {
                yield return $"\"{Collapse(text)}\"";
            }
        }
    }

    /// <summary>
    /// Two or more Latin letters in a row, once HTML entities are out of the way — <c>&amp;times;</c>
    /// and <c>&amp;larr;</c> are glyphs, not words, and are the same glyph in either language.
    /// </summary>
    private static bool HasWords(string text)
    {
        var withoutEntities = Regex.Replace(text, "&[#a-zA-Z0-9]+;", " ");

        return Regex.Matches(withoutEntities, "[A-Za-z]{2,}")
            .Any(word => !NotCopy.Contains(word.Value));
    }

    /// <summary>
    /// Everything that is not markup a reader sees: Razor comments, the script and style blocks
    /// (which are localized by having their copy handed in from the server, and are full of
    /// English identifiers either way), and <c>@{ … }</c> code blocks.
    /// </summary>
    private static string Strip(string view)
    {
        view = Regex.Replace(view, @"@\*.*?\*@", " ", RegexOptions.Singleline);
        view = Regex.Replace(view, "<script\\b.*?</script>", " ",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        view = Regex.Replace(view, "<style\\b.*?</style>", " ",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        return StripCodeBlocks(view);
    }

    /// <summary>
    /// Drops every <c>@{ … }</c> block, braces balanced so a nested one does not end the outer.
    /// Razor control blocks (<c>@if</c>, <c>@foreach</c>) are left alone on purpose: they hold the
    /// markup, so removing them would take most of the views with them. The C# statements stranded
    /// inside them are what <see cref="CodeIsh"/> is for.
    /// </summary>
    private static string StripCodeBlocks(string view)
    {
        var kept = new StringBuilder();
        var index = 0;

        while (index < view.Length)
        {
            var start = view.IndexOf("@{", index, StringComparison.Ordinal);
            if (start < 0)
            {
                kept.Append(view, index, view.Length - index);
                break;
            }

            kept.Append(view, index, start - index);

            var depth = 0;
            var scan = start + 1;
            for (; scan < view.Length; scan++)
            {
                if (view[scan] == '{')
                {
                    depth++;
                }
                else if (view[scan] == '}' && --depth == 0)
                {
                    scan++;
                    break;
                }
            }

            index = scan;
        }

        return kept.ToString();
    }

    private static string Collapse(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string FindViewsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "MedHistory", "Views");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate MedHistory/Views by walking up from {AppContext.BaseDirectory}");
    }
}
