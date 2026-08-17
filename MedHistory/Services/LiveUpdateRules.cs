namespace MedHistory.Services;

/// <summary>
/// Whether a request wants a redirect or a fragment. The day page's checklist controls are
/// plain forms that a script upgrades to fetch, so both callers hit the same URL under the
/// same rules and only the response shape differs — which means the decision has to come off
/// the request itself. It is deliberately the header and nothing else: a post that arrives
/// without it (script off, or a failed fetch handed back to the browser) is a real navigation
/// and must get the redirect, never a headless fragment rendered into the address bar.
/// </summary>
public static class LiveUpdateRules
{
    /// <summary>
    /// The value the day page's fetch sends — the long-standing convention every framework
    /// already sniffs for, so nothing bespoke has to be taught to the client.
    /// </summary>
    public const string FragmentHeaderValue = "XMLHttpRequest";

    /// <summary>
    /// True only for that one value. Casing is ignored because it travels as a header value
    /// and no client is obliged to preserve it; anything else, including a missing or empty
    /// header, is an ordinary request.
    /// </summary>
    public static bool IsFragmentRequest(string? requestedWith) =>
        string.Equals(requestedWith, FragmentHeaderValue, StringComparison.OrdinalIgnoreCase);
}
