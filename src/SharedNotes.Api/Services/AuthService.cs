using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using SharedNotes.Api.Data;
using SharedNotes.Api.DTOs;
using SharedNotes.Api.Identity;
using SharedNotes.Api.Middleware;
using SharedNotes.Api.Models;

namespace SharedNotes.Api.Services;

public class AuthService(AppDbContext db, CurrentEmployeeAccessor current)
{
    public async Task<Employee> LoginAsync(string email, string password)
    {
        var normalized = email.Trim();
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Email == normalized);
        // Always run a hash verification (against a dummy hash if needed) so timing does not reveal valid emails.
        var ok = PasswordHasher.Verify(password, employee?.PasswordHash);
        if (!ok || employee is null) throw ApiException.Unauthorized("Invalid email or password.");
        return employee;
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest r)
    {
        var employee = await db.Employees.FirstAsync(e => e.EmployeeId == current.Required.EmployeeId);
        if (!PasswordHasher.Verify(r.CurrentPassword, employee.PasswordHash))
            throw ApiException.BadRequest("Current password is incorrect.");
        employee.PasswordHash = PasswordHasher.Hash(r.NewPassword);
        await db.SaveChangesAsync();
    }

    public static Task SignInAsync(HttpContext http, Employee employee)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, employee.EmployeeId.ToString())],
            CookieAuthenticationDefaults.AuthenticationScheme);
        return http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    }
}
