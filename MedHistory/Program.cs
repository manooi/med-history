using MedHistory.Data;
using MedHistory.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

// .env (repo root or MedHistory/) feeds ConnectionStrings__Default and Auth__Password
// into the process env before the config providers read it.
DotNetEnv.Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

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

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Static assets must stay reachable pre-login (e.g. site.css on the login page itself).
app.MapStaticAssets().AllowAnonymous();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Day}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
