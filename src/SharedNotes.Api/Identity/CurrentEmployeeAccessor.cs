using SharedNotes.Api.Middleware;
using SharedNotes.Api.Models;

namespace SharedNotes.Api.Identity;

/// <summary>Holds the signed-in employee for the current request (set by EmployeeIdentityMiddleware).</summary>
public class CurrentEmployeeAccessor
{
    public Employee? Employee { get; set; }

    public Employee Required =>
        Employee ?? throw new InvalidOperationException("No current employee. Identity middleware did not run.");

    public bool IsAdmin => Required.Role == EmployeeRole.Admin;

    public void RequireAdmin()
    {
        if (!IsAdmin) throw ApiException.Forbidden("This action requires an administrator.");
    }
}
