using System.Text.RegularExpressions;

namespace MedHistory.Tests;

/// <summary>
/// A date formatted without an explicit culture is the one i18n mistake in this app that does not
/// look like a mistake. Under th-TH the ambient calendar is Buddhist-era, so
/// <c>day.ToString("yyyy-MM-dd")</c> writes <c>2569-08-22</c>: a day key that parses cleanly back
/// as the year 2569, a link 543 years out, no exception anywhere. So the rule is mechanical —
/// every date/time format string in MedHistory names its culture, invariant for identifiers
/// (<see cref="MedHistory.Services.AppTime.Key"/> and friends) and the reader's for labels
/// (<see cref="MedHistory.Services.AppTime.DayLabel"/>) — and this walks the source to enforce it,
/// the way <see cref="ConfirmDialogUsageTests"/> walks the views for native confirm().
/// </summary>
public class CultureFormattingUsageTests
{
    /// <summary><c>x.ToString("…")</c> with nothing after the format — the one-argument overload
    /// takes the ambient culture.</summary>
    private static readonly Regex BareToString =
        new(@"ToString\(\s*""((?:[^""\\]|\\.)*)""\s*\)");

    /// <summary><c>string.Format("…", …)</c> whose first argument is the format rather than a
    /// culture.</summary>
    private static readonly Regex BareStringFormat =
        new(@"\bstring\.Format\(\s*""((?:[^""\\]|\\.)*)""", RegexOptions.IgnoreCase);

    /// <summary>An interpolated string, which is always formatted against the ambient culture
    /// unless the whole expression is wrapped.</summary>
    private static readonly Regex InterpolatedString =
        new(@"\$@?""((?:[^""\\]|\\.)*)""");

    private static readonly Regex Hole = new(@"\{([^{}]*)\}");

    /// <summary>The custom date and time specifiers. A custom numeric format carries none of
    /// them — its alphabet is digits, <c>#</c> and separators — so a decimal format is not
    /// mistaken for a date one.</summary>
    private const string DateSpecifiers = "yMdHhmstfFgKz";

    /// <summary>Wrapping an interpolated string in either of these is how it gets an explicit
    /// culture, so a line that does is not an offender.</summary>
    private static readonly string[] ExplicitInterpolation =
        ["FormattableString.Invariant", "string.Create("];

    [Fact]
    public void NoSourceFileFormatsADateWithoutAnExplicitCulture()
    {
        var sourceDir = FindSourceDirectory();

        var offenders = SourceFiles(sourceDir)
            .SelectMany(path => File.ReadAllLines(path)
                .Select((line, index) => (Path: path, Number: index + 1, Text: line))
                .Where(line => FormatsADateWithoutACulture(line.Text)))
            .Select(line => $"{Path.GetRelativePath(sourceDir, line.Path)}:{line.Number}  {line.Text.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "Date/time format with no CultureInfo argument in:" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders) + Environment.NewLine +
            "Pass CultureInfo.InvariantCulture for an identifier (URL segment, form value) or " +
            "CultureInfo.CurrentUICulture for a label a reader sees.");
    }

    // The scanner's own behaviour, pinned so the guard cannot decay into one that passes
    // everything. The false cases are the ones that matter: a guard nobody can trip is worse
    // than none, because it reads like cover.

    [Theory]
    [InlineData(@"var key = day.ToString(""yyyy-MM-dd"");")]
    [InlineData(@"var label = day.ToString(""ddd d MMM yyyy"");")]
    [InlineData(@"var time = instant.ToString(""HH:mm"");")]
    [InlineData(@"var label = $""{day:yyyy-MM-dd}"";")]
    [InlineData(@"var label = $""day {day:d MMMM yyyy} done"";")]
    [InlineData(@"var s = string.Format(""{0:yyyy-MM}"", month);")]
    public void TheScannerCatches(string line)
    {
        Assert.True(FormatsADateWithoutACulture(line));
    }

    [Theory]
    [InlineData(@"var key = day.ToString(""yyyy-MM-dd"", CultureInfo.InvariantCulture);")]
    [InlineData(@"var label = day.ToString(""ddd d MMM yyyy"", culture);")]
    [InlineData(@"var s = string.Format(CultureInfo.InvariantCulture, ""{0:yyyy-MM}"", month);")]
    [InlineData(@"var s = FormattableString.Invariant($""{day:yyyy-MM-dd}"");")]
    [InlineData(@"var qty = value.ToString(""0.##"", CultureInfo.InvariantCulture);")]
    [InlineData(@"var name = level.ToString();")]
    [InlineData(@"var label = $""{ticked}/{planned} doses"";")]
    [InlineData(@"var href = $""/day/{AppTime.Key(day)}"";")]
    [InlineData(@"var word = $""{(isToday ? ""today"" : ""that day"")}"";")]
    public void TheScannerAllows(string line)
    {
        Assert.False(FormatsADateWithoutACulture(line));
    }

    /// <summary>
    /// Whether one line of source formats a date without saying which culture to format it in.
    /// Line by line rather than whole-file because a format string never spans a newline, and a
    /// line number is what makes the failure actionable.
    /// </summary>
    private static bool FormatsADateWithoutACulture(string line)
    {
        if (BareToString.Matches(line).Any(match => LooksLikeADateFormat(match.Groups[1].Value)))
        {
            return true;
        }

        if (BareStringFormat.Matches(line)
            .SelectMany(match => FormatsIn(match.Groups[1].Value))
            .Any(LooksLikeADateFormat))
        {
            return true;
        }

        if (ExplicitInterpolation.Any(line.Contains))
        {
            return false;
        }

        return InterpolatedString.Matches(line)
            .SelectMany(match => FormatsIn(match.Groups[1].Value))
            .Any(LooksLikeADateFormat);
    }

    /// <summary>The format component of every hole in a composite or interpolated format string.
    /// A hole with no format contributes nothing.</summary>
    private static IEnumerable<string> FormatsIn(string content) =>
        Hole.Matches(content.Replace("{{", string.Empty).Replace("}}", string.Empty))
            .Select(hole => FormatOf(hole.Groups[1].Value))
            .OfType<string>();

    /// <summary>
    /// What follows the first colon that is not inside brackets — the colon inside
    /// <c>{(a ? "x" : "y")}</c> belongs to the expression, and C# requires those parentheses for
    /// exactly this reason, which is what makes the bracket count enough.
    /// </summary>
    private static string? FormatOf(string hole)
    {
        var depth = 0;

        for (var i = 0; i < hole.Length; i++)
        {
            switch (hole[i])
            {
                case '(' or '[':
                    depth++;
                    break;
                case ')' or ']':
                    depth--;
                    break;
                case ':' when depth == 0:
                    return hole[(i + 1)..];
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a format string asks for any part of a date or a time. Quoted sections and
    /// backslash-escaped characters are literal text, so "at" in <c>'at' HH:mm</c> is not read as
    /// a specifier.
    /// </summary>
    private static bool LooksLikeADateFormat(string format)
    {
        var quote = '\0';

        for (var i = 0; i < format.Length; i++)
        {
            var character = format[i];

            if (character == '\\')
            {
                i++;
            }
            else if (quote != '\0')
            {
                quote = character == quote ? '\0' : quote;
            }
            else if (character is '\'' or '"')
            {
                quote = character;
            }
            else if (DateSpecifiers.Contains(character))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> SourceFiles(string sourceDir) =>
        Directory.EnumerateFiles(sourceDir, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs") || path.EndsWith(".cshtml"))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                           && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static string FindSourceDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "MedHistory");
            if (Directory.Exists(Path.Combine(candidate, "Services")))
            {
                return candidate;
            }
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate the MedHistory project by walking up from {AppContext.BaseDirectory}");
    }
}
