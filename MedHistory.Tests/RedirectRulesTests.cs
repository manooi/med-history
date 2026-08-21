using MedHistory.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;

namespace MedHistory.Tests;

public class RedirectRulesTests
{
    // The real Url.IsLocalUrl, not a stand-in: the rules inject the predicate precisely so the
    // locality decision stays the framework's, and a hand-rolled imitation here would test the
    // imitation instead of what the controllers actually pass in. UrlHelperBase.IsLocalUrl needs
    // no routes, so an otherwise empty ActionContext is enough to get at it.
    private static readonly Func<string?, bool> IsLocal =
        new UrlHelper(new Microsoft.AspNetCore.Mvc.ActionContext(
            new DefaultHttpContext(), new RouteData(), new ActionDescriptor())).IsLocalUrl;

    private const string Fallback = "/day/2026-08-21";

    // ---- Sanitize ----

    [Fact]
    public void Sanitize_LocalPath_IsKept()
    {
        Assert.Equal("/search?q=headache", RedirectRules.Sanitize("/search?q=headache", IsLocal));
    }

    [Fact]
    public void Sanitize_Null_IsNull()
    {
        Assert.Null(RedirectRules.Sanitize(null, IsLocal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Sanitize_BlankIsNull(string returnUrl)
    {
        Assert.Null(RedirectRules.Sanitize(returnUrl, IsLocal));
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("http://evil.example/day/2026-08-21")]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    [InlineData("javascript:alert(1)")]
    public void Sanitize_OffSite_IsNull(string returnUrl)
    {
        Assert.Null(RedirectRules.Sanitize(returnUrl, IsLocal));
    }

    [Fact]
    public void Sanitize_BlankIsRejectedWithoutConsultingThePredicate()
    {
        // Whitespace is settled before the predicate runs, so the rule holds for any caller's
        // notion of local, not just the one that happens to reject blanks too.
        Assert.Null(RedirectRules.Sanitize("   ", _ => true));
    }

    // ---- Resolve ----

    [Fact]
    public void Resolve_LocalPath_IsTheTarget()
    {
        Assert.Equal(
            "/type-report?types=Med&page=2",
            RedirectRules.Resolve("/type-report?types=Med&page=2", IsLocal, Fallback));
    }

    [Fact]
    public void Resolve_Null_IsTheFallback()
    {
        Assert.Equal(Fallback, RedirectRules.Resolve(null, IsLocal, Fallback));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_Blank_IsTheFallback(string returnUrl)
    {
        Assert.Equal(Fallback, RedirectRules.Resolve(returnUrl, IsLocal, Fallback));
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("http://evil.example/day/2026-08-21")]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    public void Resolve_OffSite_IsTheFallback(string returnUrl)
    {
        // The open-redirect case: an entry saved with a hand-edited returnUrl lands on the day
        // page like it always has, never on someone else's site.
        Assert.Equal(Fallback, RedirectRules.Resolve(returnUrl, IsLocal, Fallback));
    }

    // ---- WithReturnUrl ----

    [Fact]
    public void WithReturnUrl_Origin_IsAppendedEscaped()
    {
        Assert.Equal(
            "/entries/7/edit?returnUrl=%2Fsearch%3Fq%3Dhead%2Bache%26page%3D2",
            RedirectRules.WithReturnUrl("/entries/7/edit", "/search?q=head+ache&page=2"));
    }

    [Fact]
    public void WithReturnUrl_OriginWithRepeatedParameters_StaysOneValue()
    {
        // The report's types repeat; unescaped they would read as parameters of the edit link
        // itself and the selection would arrive truncated to its first type.
        var href = RedirectRules.WithReturnUrl("/entries/7/edit", "/type-report?types=Med&types=Pain");

        Assert.Equal("/entries/7/edit?returnUrl=%2Ftype-report%3Ftypes%3DMed%26types%3DPain", href);
        Assert.Single(href.Split('?'), part => part.Contains("returnUrl"));
    }

    [Fact]
    public void WithReturnUrl_NoOrigin_IsTheBareHref()
    {
        Assert.Equal("/entries/7/edit", RedirectRules.WithReturnUrl("/entries/7/edit", null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WithReturnUrl_BlankOrigin_IsTheBareHref(string returnUrl)
    {
        Assert.Equal("/entries/7/edit", RedirectRules.WithReturnUrl("/entries/7/edit", returnUrl));
    }

    // ---- round trip ----

    [Theory]
    [InlineData("/day/2026-08-21")]
    [InlineData("/search?q=head%20ache&page=2")]
    [InlineData("/type-report?types=Med&types=Pain&page=3&sort=newest")]
    public void OriginSurvivesTheLinkAndComesBackAsTheTarget(string origin)
    {
        // The whole round trip: list page builds the edit link, the form posts the value back,
        // the controller redirects to it. What the reader left is what they return to.
        var href = RedirectRules.WithReturnUrl("/entries/7/edit", origin);
        var posted = Uri.UnescapeDataString(href.Split("?returnUrl=")[1]);

        Assert.Equal(origin, RedirectRules.Resolve(posted, IsLocal, Fallback));
    }
}
