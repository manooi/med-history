using MedHistory.Data;
using MedHistory.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
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

app.UseHttpsRedirection();
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
