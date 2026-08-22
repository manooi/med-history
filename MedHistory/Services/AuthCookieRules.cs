using Microsoft.AspNetCore.Authentication;

namespace MedHistory.Services;

/// <summary>
/// Pure factory for the <see cref="AuthenticationProperties"/> passed to <c>SignInAsync</c> on
/// login. Without <c>IsPersistent = true</c> here, the cookie ASP.NET Core issues is a
/// browser-session cookie — the 30-day <c>ExpireTimeSpan</c> with sliding expiration configured
/// on the cookie handler in <c>Program.cs</c> never gets a chance to apply, because the browser
/// (mobile Safari especially) discards the cookie itself the moment it closes. The lifetime is
/// still owned entirely by the cookie options in <c>Program.cs</c>; this only tells the browser
/// to keep the cookie past the current session so that lifetime can matter.
/// </summary>
public static class AuthCookieRules
{
    /// <summary>Properties for a successful login's <c>SignInAsync</c> call.</summary>
    public static AuthenticationProperties SignInProperties() => new() { IsPersistent = true };
}
