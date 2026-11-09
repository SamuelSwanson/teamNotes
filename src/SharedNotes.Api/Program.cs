using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SharedNotes.Api.Data;
using SharedNotes.Api.Identity;
using SharedNotes.Api.Middleware;
using SharedNotes.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<NoteEventBroadcaster>();
builder.Services.AddSingleton<PresenceTracker>();
builder.Services.AddHostedService<PresenceSweeper>();
builder.Services.AddScoped<CurrentEmployeeAccessor>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddSingleton<AttachmentStorage>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<NoteService>();
builder.Services.AddScoped<AttachmentService>();
builder.Services.AddScoped<TeamService>();
builder.Services.AddScoped<EmployeeService>();
builder.Services.AddScoped<TopicService>();

// Cookie sign-in: HttpOnly, SameSite=Strict (also blocks cross-site request forgery), sliding 8h session.
// API callers get 401/403 instead of redirects. Over plain HTTP the cookie is only allowed in Development/Testing.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "sharednotes.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing")
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
    });

// Slow down password guessing: 10 login attempts per minute per client address.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

// The connection string is resolved lazily so integration tests can override it.
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var cs = sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")
             ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
    options.UseSqlite(ResolveConnectionString(cs, env.ContentRootPath));
});

var app = builder.Build();

// Apply migrations (creates the database and seed data on first run).
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DemoSeeder.SeedPasswordsAsync(db, app.Configuration);
}

// Baseline browser hardening: no MIME sniffing, no framing, no referrer leakage, and only same-origin resources.
app.Use(async (context, next) =>
{
    var h = context.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    h["Content-Security-Policy"] = "default-src 'self'; frame-ancestors 'none'; base-uri 'self'";
    await next();
});
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<EmployeeIdentityMiddleware>();
app.MapControllers();
app.Run();

// A relative database path is placed next to the project so the file is the same wherever `dotnet run` is invoked from.
static string ResolveConnectionString(string cs, string contentRoot)
{
    var csb = new SqliteConnectionStringBuilder(cs);
    var ds = csb.DataSource;
    if (!ds.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && ds != ":memory:" && !Path.IsPathRooted(ds))
        csb.DataSource = Path.Combine(contentRoot, ds);
    return csb.ToString();
}

public partial class Program;
