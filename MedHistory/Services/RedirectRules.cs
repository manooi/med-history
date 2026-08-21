namespace MedHistory.Services;

/// <summary>
/// Pure redirect-target rules — no HTTP, no database. The entry form is reachable from three
/// different lists (the day page, search, the type report) and every one of save, delete and
/// cancel has to hand the reader back to the list they opened it from, with the search query,
/// page number and report selection still on the address. The origin therefore rides through
/// the form as a <c>returnUrl</c>, which makes it posted input like any other: an off-site
/// value would turn the entry form into an open redirect, so nothing here is trusted until it
/// has passed a locality check.
///
/// That check is injected as a predicate rather than reimplemented — callers hand in
/// <c>Url.IsLocalUrl</c>, the framework's own rules for "this address stays on this site",
/// which keeps the decision testable without a request while leaving one implementation of
/// the security-relevant part.
/// </summary>
public static class RedirectRules
{
    /// <summary>
    /// The origin worth keeping, or null when there is none to trust — absent, blank, or
    /// pointing off-site. Null is what a view reads as "nothing to round-trip", so a rejected
    /// returnUrl is not merely unused but never echoed back into the page either.
    /// </summary>
    public static string? Sanitize(string? returnUrl, Func<string?, bool> isLocal) =>
        !string.IsNullOrWhiteSpace(returnUrl) && isLocal(returnUrl) ? returnUrl : null;

    /// <summary>
    /// Where a save, a delete or a cancel lands: the origin when it survives
    /// <see cref="Sanitize"/>, else the caller's own fallback — for every entry action that is
    /// the day page it has always redirected to, so a form reached without an origin behaves
    /// exactly as it did before.
    /// </summary>
    public static string Resolve(string? returnUrl, Func<string?, bool> isLocal, string fallback) =>
        Sanitize(returnUrl, isLocal) ?? fallback;

    /// <summary>
    /// A link to <paramref name="href"/> carrying the address it is being clicked from. The
    /// origin is escaped whole, so a search query or the report's repeated <c>types</c>
    /// parameters travel as one opaque value instead of leaking their own <c>&amp;</c> and
    /// <c>=</c> into the link's query and arriving truncated. <paramref name="href"/> is a
    /// bare path by construction (an entry's edit URL), so the separator is always <c>?</c>.
    /// </summary>
    public static string WithReturnUrl(string href, string? returnUrl) =>
        string.IsNullOrWhiteSpace(returnUrl)
            ? href
            : $"{href}?returnUrl={Uri.EscapeDataString(returnUrl)}";
}
