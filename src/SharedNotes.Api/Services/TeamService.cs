using Microsoft.EntityFrameworkCore;
using SharedNotes.Api.Data;
using SharedNotes.Api.DTOs;
using SharedNotes.Api.Identity;
using SharedNotes.Api.Middleware;
using SharedNotes.Api.Models;

namespace SharedNotes.Api.Services;

public class TeamService(AppDbContext db, CurrentEmployeeAccessor current, NotificationService notifications)
{
    public const string LeadRole = "Lead";

    public Task<List<TeamDto>> ListAsync() => Project(db.Teams.AsNoTracking().OrderBy(t => t.Name)).ToListAsync();

    public async Task<TeamDto> GetAsync(int id) =>
        await Project(db.Teams.AsNoTracking().Where(t => t.TeamId == id)).FirstOrDefaultAsync()
        ?? throw ApiException.NotFound($"Team {id} not found.");

    /// <summary>Creates a team and adds the creator as its first member so they can immediately write Team notes.</summary>
    public async Task<TeamDto> CreateAsync(TeamRequest r)
    {
        var name = r.Name.Trim();
        if (await db.Teams.AnyAsync(t => t.Name == name)) throw ApiException.Conflict($"A team named '{name}' already exists.");

        var team = new Team { Name = name, CreatedAt = DateTime.UtcNow };
        team.Members.Add(new TeamMember { EmployeeId = current.Required.EmployeeId, Role = LeadRole });
        db.Teams.Add(team);
        await db.SaveChangesAsync();
        return await GetAsync(team.TeamId);
    }

    public async Task<TeamDto> AddMemberAsync(int teamId, AddMemberRequest r)
    {
        if (!await db.Teams.AnyAsync(t => t.TeamId == teamId)) throw ApiException.NotFound($"Team {teamId} not found.");
        await EnsureCanManageAsync(teamId);
        var employeeId = r.EmployeeId!.Value;
        if (!await db.Employees.AnyAsync(e => e.EmployeeId == employeeId))
            throw ApiException.BadRequest($"Employee {employeeId} does not exist.");
        if (await db.TeamMembers.AnyAsync(m => m.TeamId == teamId && m.EmployeeId == employeeId))
            throw ApiException.Conflict($"Employee {employeeId} is already a member of team {teamId}.");

        db.TeamMembers.Add(new TeamMember { TeamId = teamId, EmployeeId = employeeId, Role = r.Role?.Trim() });
        await db.SaveChangesAsync();   // the composite key also blocks duplicates if two requests race (-> 409)
        var team = await GetAsync(teamId);
        await notifications.NotifyAsync([employeeId], NotificationTypes.TeamAdded,
            $"{current.Required.Name} added you to the team {team.Name}", teamId: teamId);
        return team;
    }

    public async Task RemoveMemberAsync(int teamId, int employeeId)
    {
        if (!await db.Teams.AnyAsync(t => t.TeamId == teamId)) throw ApiException.NotFound($"Team {teamId} not found.");
        await EnsureCanManageAsync(teamId);
        var member = await db.TeamMembers.FirstOrDefaultAsync(m => m.TeamId == teamId && m.EmployeeId == employeeId)
                     ?? throw ApiException.NotFound($"Employee {employeeId} is not a member of team {teamId}.");
        db.TeamMembers.Remove(member);
        await db.SaveChangesAsync();
    }

    /// <summary>Membership is managed by administrators and by the team's leads (members with Role "Lead").</summary>
    private async Task EnsureCanManageAsync(int teamId)
    {
        if (current.IsAdmin) return;
        var me = current.Required.EmployeeId;
        if (!await db.TeamMembers.AnyAsync(m => m.TeamId == teamId && m.EmployeeId == me && m.Role == LeadRole))
            throw ApiException.Forbidden("Only administrators and team leads can manage team members.");
    }

    private static IQueryable<TeamDto> Project(IQueryable<Team> q) => q.Select(t => new TeamDto
    {
        TeamId = t.TeamId,
        Name = t.Name,
        CreatedAt = t.CreatedAt,
        Members = t.Members.OrderBy(m => m.Employee.Name)
            .Select(m => new MemberDto { EmployeeId = m.EmployeeId, Name = m.Employee.Name, Email = m.Employee.Email, Role = m.Role })
            .ToList(),
    });
}
