using MedHistory.Services;

namespace MedHistory.Tests;

public class AuthCookieRulesTests
{
    // Regression guard for the session-cookie bug: a successful login must ask for a persistent
    // cookie, or the 30-day sliding ExpireTimeSpan configured in Program.cs never gets to apply.
    [Fact]
    public void SignInProperties_IsPersistent()
    {
        Assert.True(AuthCookieRules.SignInProperties().IsPersistent);
    }
}
