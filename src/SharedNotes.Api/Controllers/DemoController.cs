using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SharedNotes.Api.Data;
using SharedNotes.Api.Services;

namespace SharedNotes.Api.Controllers;

/// <summary>
/// Demo quick-switch: lists demo accounts and signs in as one WITHOUT a password so reviewers can hop between
/// employees. Disabled (404) unless configuration <c>Demo:QuickSwitch</c> is true, which only
/// appsettings.Development.json sets. Never enable this in production.
/// </summary>
[ApiController]
[Route("api/demo")]
public class DemoController(AppDbContext db, IConfiguration config) : ControllerBase
{
    private bool Enabled => config.GetValue<bool>("Demo:QuickSwitch");

    [HttpGet("info")]
    public async Task<ActionResult> Info()
    {
        if (!Enabled) return NotFound();
        var employees = await db.Employees.AsNoTracking().OrderBy(e => e.EmployeeId)
            .Select(e => new { e.EmployeeId, e.Name, e.Email, Role = e.Role.ToString() }).ToListAsync();
        return Ok(new { password = config["Demo:Password"], employees });
    }

    [HttpPost("login/{employeeId:int}")]
    public async Task<IActionResult> Login(int employeeId)
    {
        if (!Enabled) return NotFound();
        var employee = await db.Employees.FindAsync(employeeId);
        if (employee is null) return NotFound();
        await AuthService.SignInAsync(HttpContext, employee);
        return NoContent();
    }
}
