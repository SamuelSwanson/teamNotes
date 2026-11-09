using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using SharedNotes.Api.Data;
using SharedNotes.Api.Identity;

namespace SharedNotes.Api.Middleware;

/// <summary>
/// Resolves the signed-in employee for every /api request from the authentication cookie.
/// The employee is re-read from the database each time, so role changes and removed accounts take effect immediately.
/// Missing or invalid sign-in produces 401.
/// <para>
/// Test-only escape hatch: when configuration <c>Auth:AllowHeaderIdentity</c> is true (never set in the shipped
/// appsettings), an <c>X-Employee-Id</c> header is accepted instead of a cookie. It exists so integration tests can act
/// as any employee without a password round-trip, and must never be enabled in production.
/// </para>
/// </summary>
public class EmployeeIdentityMiddleware(RequestDelegate next, IConfiguration configuration)
{
    public const string HeaderName = "X-Employee-Id";
    private readonly bool _allowHeader = configuration.GetValue<bool>("Auth:AllowHeaderIdentity");

    // Endpoints that must work before someone is signed in.
    private static readonly string[] Anonymous = ["/api/auth/login", "/api/auth/logout", "/api/demo"];

    public async Task InvokeAsync(HttpContext context, AppDbContext db, CurrentEmployeeAccessor accessor)
    {
        var path = context.Request.Path;
        if (!path.StartsWithSegments("/api") || Anonymous.Any(a => path.StartsWithSegments(a)))
        {
            await next(context);
            return;
        }

        var id = FromCookie(context) ?? FromHeader(context);
        if (id is null)
        {
            await ProblemWriter.WriteAsync(context, StatusCodes.Status401Unauthorized, "Unauthorized", "Sign in is required.");
            return;
        }

        var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.EmployeeId == id);
        if (employee is null)
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            await ProblemWriter.WriteAsync(context, StatusCodes.Status401Unauthorized, "Unauthorized", "Sign in is required.");
            return;
        }

        accessor.Employee = employee;
        await next(context);
    }

    private static int? FromCookie(HttpContext context) =>
        context.User.Identity?.IsAuthenticated == true && int.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id : null;

    private int? FromHeader(HttpContext context) =>
        _allowHeader && context.Request.Headers.TryGetValue(HeaderName, out var raw) && int.TryParse(raw, out var id)
            ? id : null;
}
