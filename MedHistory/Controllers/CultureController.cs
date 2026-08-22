using MedHistory.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace MedHistory.Controllers;

/// <summary>
/// The one place the culture cookie is written. Anonymous on purpose: the toggle has to work on
/// the login screen too, and a reader who cannot read the passcode prompt cannot log in to change
/// the language.
/// </summary>
[AllowAnonymous]
public class CultureController : Controller
{
    [HttpPost("/culture")]
    [ValidateAntiForgeryToken]
    public IActionResult Set(string? culture, string? returnUrl)
    {
        // Posted input, so untrusted: an unsupported name falls back to the default rather than
        // throwing out of CultureInfo, which would turn a hand-edited form into a 500.
        var chosen = CultureRules.IsSupported(culture) ? culture! : CultureRules.Default;

        // Written by the server and read by RequestLocalizationMiddleware; nothing client-side
        // touches it, so the sample's HttpOnly = false opt-out has no reason to be here. Essential
        // because it carries a preference the reader asked for, not tracking.
        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(chosen)),
            new CookieOptions
            {
                IsEssential = true,
                Expires = DateTimeOffset.UtcNow.AddYears(1),
            });

        // The toggle rides on every page, so returnUrl is whatever address it was clicked from —
        // posted input like any other. RedirectRules drops an off-site value instead of following
        // it, which is what keeps the toggle from becoming an open redirect.
        return Redirect(RedirectRules.Resolve(returnUrl, Url.IsLocalUrl, "/"));
    }
}
