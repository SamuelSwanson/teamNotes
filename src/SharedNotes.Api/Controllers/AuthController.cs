using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SharedNotes.Api.DTOs;
using SharedNotes.Api.Identity;
using SharedNotes.Api.Services;

namespace SharedNotes.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AuthService auth, EmployeeService employees, CurrentEmployeeAccessor current) : ControllerBase
{
    /// <summary>Signs in with email + password and sets an HttpOnly session cookie.</summary>
    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<EmployeeDto>> Login(LoginRequest request)
    {
        var employee = await auth.LoginAsync(request.Email, request.Password);
        await AuthService.SignInAsync(HttpContext, employee);
        return Ok(await employees.GetAsync(employee.EmployeeId));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    [HttpGet("me")]
    public async Task<ActionResult<EmployeeDto>> Me() => Ok(await employees.GetAsync(current.Required.EmployeeId));

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        await auth.ChangePasswordAsync(request);
        return NoContent();
    }
}
