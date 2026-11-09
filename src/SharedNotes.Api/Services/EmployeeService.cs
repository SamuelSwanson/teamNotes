using Microsoft.EntityFrameworkCore;
using SharedNotes.Api.Data;
using SharedNotes.Api.DTOs;
using SharedNotes.Api.Middleware;
using SharedNotes.Api.Models;

namespace SharedNotes.Api.Services;

public class EmployeeService(AppDbContext db)
{
    public Task<List<EmployeeDto>> ListAsync() => Project(db.Employees.AsNoTracking().OrderBy(e => e.Name)).ToListAsync();

    public async Task<EmployeeDto> GetAsync(int id) =>
        await Project(db.Employees.AsNoTracking().Where(e => e.EmployeeId == id)).FirstOrDefaultAsync()
        ?? throw ApiException.NotFound($"Employee {id} not found.");

    public async Task<EmployeeDto> CreateAsync(EmployeeRequest r)
    {
        var email = r.Email.Trim();
        if (await db.Employees.AnyAsync(e => e.Email == email))
            throw ApiException.Conflict($"An employee with email '{email}' already exists.");

        var employee = new Employee { Name = r.Name.Trim(), Email = email, CreatedAt = DateTime.UtcNow };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return await GetAsync(employee.EmployeeId);
    }

    private static IQueryable<EmployeeDto> Project(IQueryable<Employee> q) => q.Select(e => new EmployeeDto
    {
        EmployeeId = e.EmployeeId,
        Name = e.Name,
        Email = e.Email,
        CreatedAt = e.CreatedAt,
        Teams = e.Memberships.OrderBy(m => m.Team.Name)
            .Select(m => new TeamSummaryDto { TeamId = m.TeamId, Name = m.Team.Name, Role = m.Role })
            .ToList(),
    });
}
