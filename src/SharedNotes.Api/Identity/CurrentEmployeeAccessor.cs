using SharedNotes.Api.Models;

namespace SharedNotes.Api.Identity;

/// <summary>
/// Holds the employee resolved from the X-Employee-Id header for the current request.
/// DEMONSTRATION IDENTITY ONLY: the header is not authenticated and must never be used in production.
/// </summary>
public class CurrentEmployeeAccessor
{
    public Employee? Employee { get; set; }

    public Employee Required =>
        Employee ?? throw new InvalidOperationException("No current employee. Identity middleware did not run.");
}
