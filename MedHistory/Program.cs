using System.Globalization;
using System.Net;
using MedHistory;
using MedHistory.Data;
using MedHistory.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

// .env (repo root or MedHistory/) feeds ConnectionStrings__Default and Auth__Password
// into the process env before the config providers read it.
DotNetEnv.Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews()
    .AddViewLocalization()
    // Left to itself, a DataAnnotations message is looked up in a resource named after the model
    // it annotates — Resources/Models/LoginViewModel.th.resx — which would be one file per model
    // holding one sentence each. They are pointed at the shared file instead, where the rest of
    // the server's validation copy already lives, and the ErrorMessage stays the key the way
    // every other resource key in the app is its own English source text.
    .AddDataAnnotationsLocalization(options =>
        options.DataAnnotationLocalizerProvider = (_, factory) => factory.Create(typeof(SharedResource)));

// UI strings live in .resx files under MedHistory/Resources; which one a request reads is
// decided per request by UseRequestLocalization further down.
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = CultureRules.Supported
        .Select(name => new CultureInfo(name))
        .ToList();

    options.DefaultRequestCulture = new RequestCulture(CultureRules.Default);
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;

    // The culture cookie and nothing else. The default provider list also consults the
    // Accept-Language header, which would let a Thai-configured browser silently render the app —
    // including the doctor report someone prints and hands over — in a language the reader never
    // asked for. The language changes when the toggle says so, and never otherwise.
    options.RequestCultureProviders.Clear();
    options.RequestCultureProviders.Add(new CookieRequestCultureProvider());
});

var connectionString = builder.Configuration.GetConnectionString("Default");

builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));

// Log records also land in the Logs table. Console logging is untouched; levels
// for this provider come from the "Logging:DbLogger" section.
builder.Services.AddHttpContextAccessor();

var dbLoggerProvider = new DbLoggerProvider(connectionString);
builder.Logging.AddProvider(dbLoggerProvider);

// The application name must stay fixed: it is the key-ring's purpose discriminator,
// so changing it would orphan every key already persisted in the database.
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<AppDbContext>()
    .SetApplicationName("medhistory");

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

// Single-user personal app: every request must be authenticated unless the
// action opts out with [AllowAnonymous] (login page, static assets).
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// Cloud Run terminates TLS at its own proxy and forwards the original scheme/client IP
// via X-Forwarded-Proto / X-Forwarded-For. That proxy isn't a fixed, known address, so
// the default KnownNetworks/KnownProxies allowlist (loopback only) would reject the
// headers — clearing both is the standard approach for single-hop PaaS proxies like
// Cloud Run.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Caps how many login attempts a single IP can make per window, ahead of (and independent of)
// the password-based LoginThrottleRules lockout further down the stack.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, cancellationToken) =>
    {
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfterValue)
            ? retryAfterValue
            : (TimeSpan?)null;

        context.HttpContext.Response.Headers.RetryAfter =
            RateLimitRules.RetryAfterSeconds(retryAfter).ToString();
        context.HttpContext.Response.ContentType = "text/plain";

        await context.HttpContext.Response.WriteAsync(
            "Too many requests. Try again shortly.", cancellationToken);
    };

    options.AddPolicy(RateLimitRules.PolicyName, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            RateLimitRules.PartitionKey(httpContext.Connection.RemoteIpAddress),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = RateLimitRules.PermitLimit,
                Window = TimeSpan.FromSeconds(RateLimitRules.WindowSeconds),
                QueueLimit = 0,
            }));
});

var app = builder.Build();

dbLoggerProvider.HttpContextAccessor = app.Services.GetRequiredService<IHttpContextAccessor>();

// A provider handed to AddProvider as an instance is owned by neither the
// container nor the logger factory, so nothing else would ever drain its queue.
app.Lifetime.ApplicationStopped.Register(dbLoggerProvider.Dispose);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// Must run before anything that inspects Request.Scheme or RemoteIpAddress (HTTPS
// redirection, auth) so Cloud Run's X-Forwarded-* headers take effect first.
app.UseForwardedHeaders();

// On Cloud Run the scheme arrives via X-Forwarded-Proto (applied above), so this
// redirect is a harmless no-op there. Skip it when containerized — the aspnet base
// image sets DOTNET_RUNNING_IN_CONTAINER, and a local `docker run` has no proxy in
// front, so redirecting would just loop on plain http. Plain local `dotnet run`
// leaves the variable unset, so the redirect stays active for that flow.
var isRunningInContainer = string.Equals(
    Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
    "true",
    StringComparison.OrdinalIgnoreCase);

if (!isRunningInContainer)
{
    app.UseHttpsRedirection();
}

// Ahead of routing and everything that renders, so CurrentCulture/CurrentUICulture are already
// the reader's choice by the time a view, a validation message or an error page is produced.
app.UseRequestLocalization();

app.UseRouting();

// Must run after UseRouting (endpoint-aware policy resolution needs the matched endpoint's
// [EnableRateLimiting] metadata) and after UseForwardedHeaders above, so
// Connection.RemoteIpAddress is already the real client IP behind Cloud Run's proxy, not the
// proxy's own address.
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

// Static assets must stay reachable pre-login (e.g. site.css on the login page itself).
app.MapStaticAssets().AllowAnonymous();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Day}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
