using Microsoft.Extensions.Logging;

namespace MedHistory.Services;

/// <summary>
/// Pure decisions the database logger makes about a log record before it is
/// queued — no I/O, no configuration. Level thresholds themselves come from
/// the standard "Logging:DbLogger" section; what lives here is the guard that
/// must hold no matter how that section is configured.
/// </summary>
public static class DbLogFilter
{
    public const string EntityFrameworkCategoryPrefix = "Microsoft.EntityFrameworkCore";

    /// <summary>
    /// False for records the provider refuses outright. EF Core below Warning is
    /// dropped because every insert this provider makes would otherwise be
    /// narrated by EF into the same queue.
    /// </summary>
    public static bool ShouldWrite(string? category, LogLevel level)
    {
        if (level == LogLevel.None)
        {
            return false;
        }

        if (level < LogLevel.Warning &&
            category is not null &&
            category.StartsWith(EntityFrameworkCategoryPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Clips a value to its column width. The insert is raw SQL, so an
    /// over-long category or path would come back as a database error rather
    /// than a truncation.
    /// </summary>
    public static string? Truncate(string? value, int maxLength) =>
        value is not null && value.Length > maxLength ? value[..maxLength] : value;
}
