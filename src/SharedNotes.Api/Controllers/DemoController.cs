using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SharedNotes.Api.Data;

namespace SharedNotes.Api.Controllers;

/// <summary>
/// Demo-only helper that needs no identity header: it lets the browser UI populate the
/// "act as" dropdown. A real deployment would replace this with an actual login.
/// </summary>
[ApiController]
[Route("api/demo")]
public class DemoController(AppDbContext db) : ControllerBase
{
    [HttpGet("employees")]
    public async Task<ActionResult> Employees() =>
        Ok(await db.Employees.AsNoTracking().OrderBy(e => e.EmployeeId)
            .Select(e => new { e.EmployeeId, e.Name }).ToListAsync());
}
