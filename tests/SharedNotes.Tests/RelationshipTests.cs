using System.Net;

namespace SharedNotes.Tests;

public record Member(int EmployeeId, string Name, string Email, string? Role);
public record Team(int TeamId, string Name, List<Member> Members);
public record TeamSummary(int TeamId, string Name, string? Role);
public record Employee(int EmployeeId, string Name, string Email, string Role, List<TeamSummary> Teams);

public class RelationshipTests : IDisposable
{
    private readonly AppFactory _factory = new();
    private readonly HttpClient _alice;

    public RelationshipTests() => _alice = _factory.ClientFor(1);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Employee_CanBelongToMultipleTeams()
    {
        var alice = await (await _alice.GetAsync("/api/employees/1")).ReadAsync<Employee>();
        Assert.Equal([1, 2], alice.Teams.Select(t => t.TeamId).Order());
    }

    [Fact]
    public async Task AddMember_ThenRemove_UpdatesTeam()
    {
        var added = await (await _alice.PostJsonAsync("/api/teams/1/members", new { employeeId = 4, role = "Guest" }))
            .ReadAsync<Team>(HttpStatusCode.Created);
        Assert.Contains(added.Members, m => m.EmployeeId == 4 && m.Role == "Guest");

        Assert.Equal(HttpStatusCode.NoContent, (await _alice.DeleteAsync("/api/teams/1/members/4")).StatusCode);
        var team = await (await _alice.GetAsync("/api/teams/1")).ReadAsync<Team>();
        Assert.DoesNotContain(team.Members, m => m.EmployeeId == 4);

        Assert.Equal(HttpStatusCode.NotFound, (await _alice.DeleteAsync("/api/teams/1/members/4")).StatusCode);
    }

    [Fact]
    public async Task DuplicateMembership_Returns409_AndLeavesSingleRow()
    {
        var response = await _alice.PostJsonAsync("/api/teams/1/members", new { employeeId = 2 });   // Bob is already in Engineering
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var team = await (await _alice.GetAsync("/api/teams/1")).ReadAsync<Team>();
        Assert.Single(team.Members, m => m.EmployeeId == 2);
    }

    [Fact]
    public async Task AddMember_UnknownEmployeeOrTeam_IsRejected()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _alice.PostJsonAsync("/api/teams/1/members", new { employeeId = 999 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _alice.PostJsonAsync("/api/teams/999/members", new { employeeId = 2 })).StatusCode);
    }

    [Fact]
    public async Task CreateTeam_AddsCreatorAsMember_AndRejectsDuplicateName()
    {
        var team = await (await _alice.PostJsonAsync("/api/teams", new { name = "Design" })).ReadAsync<Team>(HttpStatusCode.Created);
        Assert.Equal([1], team.Members.Select(m => m.EmployeeId));
        Assert.Equal(HttpStatusCode.Conflict, (await _alice.PostJsonAsync("/api/teams", new { name = "design" })).StatusCode);
    }

    [Fact]
    public async Task CreateEmployee_Works_AndDuplicateEmailIsRejected()
    {
        var created = await (await _alice.PostJsonAsync("/api/employees", new { name = "Eve", email = "eve@example.com", password = "EvePassword1" }))
            .ReadAsync<Employee>(HttpStatusCode.Created);
        Assert.Equal("Eve", created.Name);
        Assert.Equal(HttpStatusCode.Conflict, (await _alice.PostJsonAsync("/api/employees", new { name = "Eve2", email = "EVE@example.com", password = "EvePassword1" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _alice.PostJsonAsync("/api/employees", new { name = "Bad", email = "not-an-email", password = "EvePassword1" })).StatusCode);
    }

    [Fact]
    public async Task DuplicateTopicName_IsRejected_CaseInsensitively()
    {
        Assert.Equal(HttpStatusCode.Created, (await _alice.PostJsonAsync("/api/topics", new { name = "Budget" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _alice.PostJsonAsync("/api/topics", new { name = "budget" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _alice.PostJsonAsync("/api/topics", new { name = "Planning" })).StatusCode);   // seeded
    }

    [Fact]
    public async Task UpdateTopic_RenameToExistingName_Returns409()
    {
        var ok = await _alice.PutJsonAsync("/api/topics/3", new { name = "Brainstorms", description = "d" });
        Assert.Equal("Brainstorms", (await ok.ReadAsync<Topic>()).Name);
        Assert.Equal(HttpStatusCode.Conflict, (await _alice.PutJsonAsync("/api/topics/3", new { name = "Planning" })).StatusCode);
    }

    [Fact]
    public async Task Note_WithUnknownTopic_IsRejected()
    {
        var response = await _alice.PostJsonAsync("/api/notes",
            new { title = "t", content = "c", visibility = "All", topicIds = new[] { 1, 999 } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // And nothing was created.
        var notes = await (await _alice.GetAsync("/api/notes?search=t")).ReadAsync<List<Note>>();
        Assert.DoesNotContain(notes, n => n.Title == "t");
    }

    [Fact]
    public async Task DeleteTopic_RemovesLinks_ButKeepsNotes()
    {
        // Seed note 2 carries topics 1 (Planning) and 2 (Meeting Notes).
        Assert.Equal(HttpStatusCode.NoContent, (await _alice.DeleteAsync("/api/topics/1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _alice.GetAsync("/api/topics/1")).StatusCode);

        var note = await (await _alice.GetAsync("/api/notes/2")).ReadAsync<Note>();
        Assert.Equal([2], note.Topics.Select(t => t.TopicId));
        Assert.Equal(HttpStatusCode.OK, (await _alice.GetAsync("/api/notes/1")).StatusCode);   // note 1 also had topic 1
    }

    [Fact]
    public async Task DeleteNote_DoesNotDeleteItsTopics()
    {
        var note = await _alice.CreateNoteAsync(topicIds: [1]);
        await _alice.DeleteAsync($"/api/notes/{note.NoteId}");
        Assert.Equal(HttpStatusCode.OK, (await _alice.GetAsync("/api/topics/1")).StatusCode);
    }
}
