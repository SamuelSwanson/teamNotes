using Microsoft.AspNetCore.Mvc;
using SharedNotes.Api.DTOs;
using SharedNotes.Api.Services;

namespace SharedNotes.Api.Controllers;

[ApiController]
[Route("api/teams")]
public class TeamsController(TeamService teams) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<TeamDto>>> List() => Ok(await teams.ListAsync());

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TeamDto>> Get(int id) => Ok(await teams.GetAsync(id));

    [HttpPost]
    public async Task<ActionResult<TeamDto>> Create(TeamRequest request)
    {
        var team = await teams.CreateAsync(request);
        return CreatedAtAction(nameof(Get), new { id = team.TeamId }, team);
    }

    [HttpPost("{id:int}/members")]
    public async Task<ActionResult<TeamDto>> AddMember(int id, AddMemberRequest request)
    {
        var team = await teams.AddMemberAsync(id, request);
        return CreatedAtAction(nameof(Get), new { id }, team);
    }

    [HttpDelete("{id:int}/members/{employeeId:int}")]
    public async Task<IActionResult> RemoveMember(int id, int employeeId)
    {
        await teams.RemoveMemberAsync(id, employeeId);
        return NoContent();
    }
}

[ApiController]
[Route("api/employees")]
public class EmployeesController(EmployeeService employees) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<EmployeeDto>>> List() => Ok(await employees.ListAsync());

    [HttpGet("{id:int}")]
    public async Task<ActionResult<EmployeeDto>> Get(int id) => Ok(await employees.GetAsync(id));

    [HttpPost]
    public async Task<ActionResult<EmployeeDto>> Create(EmployeeRequest request)
    {
        var employee = await employees.CreateAsync(request);
        return CreatedAtAction(nameof(Get), new { id = employee.EmployeeId }, employee);
    }
}

[ApiController]
[Route("api/topics")]
public class TopicsController(TopicService topics) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<TopicDto>>> List() => Ok(await topics.ListAsync());

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TopicDto>> Get(int id) => Ok(await topics.GetAsync(id));

    [HttpPost]
    public async Task<ActionResult<TopicDto>> Create(TopicRequest request)
    {
        var topic = await topics.CreateAsync(request);
        return CreatedAtAction(nameof(Get), new { id = topic.TopicId }, topic);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<TopicDto>> Update(int id, TopicRequest request) => Ok(await topics.UpdateAsync(id, request));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await topics.DeleteAsync(id);
        return NoContent();
    }
}
