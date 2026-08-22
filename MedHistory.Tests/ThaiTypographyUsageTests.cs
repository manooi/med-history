using System.Text.RegularExpressions;

namespace MedHistory.Tests;

public class ThaiTypographyUsageTests
{
    // Thai marks that sit above and below the base consonant need more vertical room than a
    // control tuned for Latin gives them, so the tight controls carry a `th:`-prefixed utility
    // that only applies under :lang(th). The variant is declared once, in Styles/site.css.
    //
    // Losing that declaration is the quiet failure this guards: Tailwind does not error on an
    // unknown variant, it simply generates nothing for `th:leading-normal`, so every Thai control
    // silently reverts to the Latin metric and only a Thai reader would ever see it. Both halves
    // are asserted, so renaming the variant on either side fails here rather than on the page.
    [Fact]
    public void EveryThaiScopedUtilityInAViewHasItsVariantDeclared()
    {
        var repoRoot = FindRepoRoot();
        var viewsDir = Path.Combine(repoRoot, "MedHistory", "Views");

        var users = Directory.EnumerateFiles(viewsDir, "*.cshtml", SearchOption.AllDirectories)
            .Where(path => Regex.IsMatch(File.ReadAllText(path), @"(?<![\w:-])th:[a-z]"))
            .Select(path => Path.GetRelativePath(viewsDir, path))
            .Order()
            .ToList();

        Assert.True(users.Count > 0,
            "No view uses the th: variant any more. If the Thai controls were reworked, retire " +
            "the variant in MedHistory/Styles/site.css and this test with them - but a silently " +
            "unused variant means the tone-mark fix has been dropped.");

        var css = File.ReadAllText(Path.Combine(repoRoot, "MedHistory", "Styles", "site.css"));

        Assert.True(Regex.IsMatch(css, @"@custom-variant\s+th\s*\("),
            "Views use th:-prefixed utilities (" + string.Join(", ", users) + ") but " +
            "MedHistory/Styles/site.css no longer declares `@custom-variant th`. Tailwind emits " +
            "nothing for an unknown variant, so those controls would fall back to the Latin " +
            "line-height under Thai with nothing failing.");

        Assert.Contains(":lang(th)", css);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "MedHistory", "Views")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate the repo root by walking up from {AppContext.BaseDirectory}");
    }
}
