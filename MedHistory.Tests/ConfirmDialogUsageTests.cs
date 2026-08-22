namespace MedHistory.Tests;

public class ConfirmDialogUsageTests
{
    // Destructive controls route through the in-app _ConfirmDialog (data-confirm="...") instead
    // of the browser's native confirm(), which the app's dark theme and print rules can't style.
    // This walks every view and fails if a native confirm() call creeps back in.
    [Fact]
    public void NoViewCallsTheBrowserNativeConfirm()
    {
        var viewsDir = FindViewsDirectory();

        var offenders = Directory.EnumerateFiles(viewsDir, "*.cshtml", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("confirm("))
            .Select(path => Path.GetRelativePath(viewsDir, path))
            .ToList();

        Assert.True(offenders.Count == 0,
            "Found native confirm() in: " + string.Join(", ", offenders) +
            " - use data-confirm=\"...\" and the shared _ConfirmDialog instead.");
    }

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
