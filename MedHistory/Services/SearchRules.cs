namespace MedHistory.Services;

/// <summary>
/// Pure search rules — no clock, no database, no HTTP. Search reuses the type report's
/// day-pagination shape (<see cref="TypeReportRules"/>) wholesale: results are paged in blocks of
/// distinct entry-days exactly like the type report, so a query that matches many entries on one
/// day still only costs one page slot. This file only adds what search itself needs on top of
/// that — normalizing the typed-in query, and escaping it for a Postgres ILIKE pattern.
/// </summary>
public static class SearchRules
{
    /// <summary>
    /// Trims the raw query string. A query that is empty or all whitespace normalizes to null,
    /// which is what tells "no search has been run yet" (bare form) apart from "a search ran and
    /// matched nothing" — only a non-null query ever reaches the database.
    /// </summary>
    public static string? NormalizeQuery(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        var trimmed = raw.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    /// <summary>
    /// Escapes the characters that are special inside a Postgres ILIKE pattern — the backslash
    /// escape character itself, then the two wildcards it can now escape — so a literal
    /// <c>%</c> or <c>_</c> typed by the user matches itself instead of acting as a wildcard.
    /// Backslash must be escaped first: escaping it after the wildcard replacements would
    /// double-escape the backslashes those replacements just introduced. The caller wraps the
    /// result in <c>%...%</c> to search for it as a substring; this only escapes the literal.
    /// </summary>
    public static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
