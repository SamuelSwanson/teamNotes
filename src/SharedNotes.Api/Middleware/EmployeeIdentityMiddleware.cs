using Microsoft.EntityFrameworkCore;
using SharedNotes.Api.Data;
using SharedNotes.Api.Identity;

namespace SharedNotes.Api.Middleware;

/// <summary>
/// Resolves the current employee from the X-Employee-Id header for every /api request
/// (except /api/demo, which exists so the UI can list employees before one is chosen).
/// Missing or unknown ids produce 401.
/// </summary>
public class EmployeeIdentityMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Employee-Id";

    public async Task InvokeAsync(HttpContext context, AppDbContext db, CurrentEmployeeAccessor accessor)
    {
        var path = context.Request.Path;
        if (!path.StartsWithSegments("/api") || path.StartsWithSegments("/api/demo"))
        {
            await next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var raw) || !int.TryParse(raw, out var id))
        {
            await ProblemWriter.WriteAsync(context, StatusCodes.Status401Unauthorized, "Unauthorized",
                $"A valid {HeaderName} header is required.");
            return;
        }

        var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.EmployeeId == id);
        if (employee is null)
        {
            await ProblemWriter.WriteAsync(context, StatusCodes.Status401Unauthorized, "Unauthorized",
                $"No employee with id {id}.");
            return;
        }

        accessor.Employee = employee;
        await next(context);
    }
}
