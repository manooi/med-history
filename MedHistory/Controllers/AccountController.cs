using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MedHistory.Data;
using MedHistory.Models;
using MedHistory.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Controllers;

public class AccountController : Controller
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AccountController> _logger;

    public AccountController(AppDbContext db, IConfiguration configuration, ILogger<AccountController> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    [AllowAnonymous]
    [HttpGet("/login")]
    public IActionResult Login(string? returnUrl = null)
    {
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [AllowAnonymous]
    [HttpPost("/login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        var configuredPassword = _configuration["Auth:Password"];

        if (string.IsNullOrEmpty(configuredPassword))
        {
            _logger.LogWarning("Login rejected: no Auth:Password is configured");
            ModelState.AddModelError(string.Empty, "Password not configured.");
            return RedisplayLogin(model);
        }

        if (!ModelState.IsValid)
        {
            return RedisplayLogin(model);
        }

        var now = DateTime.UtcNow;

        var recentFailures = await _db.LoginAttempts
            .AsNoTracking()
            .Where(a => !a.Succeeded && a.AttemptedAtUtc >= LoginThrottleRules.CutoffUtc(now))
            .Select(a => a.AttemptedAtUtc)
            .ToListAsync();

        var throttleDecision = LoginThrottleRules.Decide(recentFailures, now);

        if (throttleDecision is LoginThrottleDecision.LockedUntil lockedUntil)
        {
            // Neither the password nor a fresh attempt is recorded here — a locked-out request
            // never touches either, which is what keeps the lockout's own expiry from moving.
            var remainingMinutes = Math.Max(1, (int)Math.Ceiling((lockedUntil.UntilUtc - now).TotalMinutes));
            _logger.LogWarning("Login rejected: locked out from {RemoteAddress}",
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            ModelState.AddModelError(string.Empty, $"Too many attempts — try again in {remainingMinutes} min.");
            return RedisplayLogin(model);
        }

        var providedBytes = Encoding.UTF8.GetBytes(model.Password);
        var configuredBytes = Encoding.UTF8.GetBytes(configuredPassword);

        var isValid = providedBytes.Length == configuredBytes.Length
            && CryptographicOperations.FixedTimeEquals(providedBytes, configuredBytes);

        if (!isValid)
        {
            // The submitted password never reaches the log, here or anywhere.
            _logger.LogWarning("Login failed: incorrect password from {RemoteAddress}",
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

            _db.LoginAttempts.Add(new LoginAttempt { AttemptedAtUtc = now, Succeeded = false });
            await _db.SaveChangesAsync();

            // Fixed cost per wrong guess, locked out or not — see LoginThrottleRules.FailDelay.
            await Task.Delay(LoginThrottleRules.FailDelay);

            ModelState.AddModelError(string.Empty, "Incorrect password.");
            return RedisplayLogin(model);
        }

        // A successful login clears the failure streak outright rather than recording a success
        // row — see the comment on LoginAttempt.
        await _db.LoginAttempts.ExecuteDeleteAsync();

        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, "owner") },
            CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        _logger.LogInformation("Login succeeded");

        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
        {
            return Redirect(model.ReturnUrl);
        }

        return RedirectToAction("Index", "Day");
    }

    [HttpPost("/logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        _logger.LogInformation("Logout");

        return Redirect("/login");
    }

    // Guards against a stray GET /logout link (e.g. an anchor instead of a form)
    // resulting in a dead 405 in the middle of the app.
    [AllowAnonymous]
    [HttpGet("/logout")]
    public IActionResult LogoutDeadLinkGuard()
    {
        return Redirect("/");
    }

    private IActionResult RedisplayLogin(LoginViewModel model)
    {
        // Never echo the submitted password back into the form.
        ModelState.Remove(nameof(LoginViewModel.Password));
        model.Password = string.Empty;
        return View("Login", model);
    }
}
