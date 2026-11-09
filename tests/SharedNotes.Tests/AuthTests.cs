using System.Net;

namespace SharedNotes.Tests;

public class AuthTests : IDisposable
{
    private readonly AppFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    /// <summary>Creates an employee (as admin Alice) with a known password and returns their email.</summary>
    private async Task<string> CreateAccountAsync(string email = "erin@example.com", string password = "CorrectHorse1", string? role = null)
    {
        var response = await _factory.ClientFor(1).PostJsonAsync("/api/employees", new { name = "Erin", email, password, role });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return email;
    }

    [Fact]
    public async Task Login_WithCorrectPassword_SignsInViaCookie_AndMeWorks()
    {
        var email = await CreateAccountAsync();
        var client = _factory.CreateClient();   // no header: cookie only

        var login = await client.PostJsonAsync("/api/auth/login", new { email, password = "CorrectHorse1" });
        var employee = await login.ReadAsync<Employee>();
        Assert.Equal(email, employee.Email);
        Assert.Contains("HttpOnly", string.Join(";", login.Headers.GetValues("Set-Cookie")), StringComparison.OrdinalIgnoreCase);

        var me = await (await client.GetAsync("/api/auth/me")).ReadAsync<Employee>();
        Assert.Equal(email, me.Email);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/notes")).StatusCode);
    }

    [Theory]
    [InlineData("erin@example.com", "wrong-password")]
    [InlineData("nobody@example.com", "CorrectHorse1")]
    public async Task Login_WithWrongCredentials_Returns401_WithSameMessage(string email, string password)
    {
        await CreateAccountAsync();
        var response = await _factory.CreateClient().PostJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Invalid email or password.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_ForAccountWithoutPassword_IsRejected()
    {
        // Seeded accounts have no password unless demo seeding is on.
        var response = await _factory.CreateClient().PostJsonAsync("/api/auth/login", new { email = "bob@notes.com", password = "anything123" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_EndsSession()
    {
        var email = await CreateAccountAsync();
        var client = _factory.CreateClient();
        await client.PostJsonAsync("/api/auth/login", new { email, password = "CorrectHorse1" });

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_RequiresCurrentPassword_AndReplacesIt()
    {
        var email = await CreateAccountAsync();
        var client = _factory.CreateClient();
        await client.PostJsonAsync("/api/auth/login", new { email, password = "CorrectHorse1" });

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostJsonAsync("/api/auth/change-password",
            new { currentPassword = "nope", newPassword = "BrandNewPass1" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostJsonAsync("/api/auth/change-password",
            new { currentPassword = "CorrectHorse1", newPassword = "BrandNewPass1" })).StatusCode);

        var fresh = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await fresh.PostJsonAsync("/api/auth/login", new { email, password = "CorrectHorse1" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fresh.PostJsonAsync("/api/auth/login", new { email, password = "BrandNewPass1" })).StatusCode);
    }

    [Fact]
    public async Task Api_WithoutSignIn_Returns401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/notes")).StatusCode);
    }

    [Fact]
    public async Task ShortPassword_IsRejectedOnEmployeeCreation()
    {
        var response = await _factory.ClientFor(1).PostJsonAsync("/api/employees", new { name = "X", email = "x@example.com", password = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PasswordHash_IsNeverReturned()
    {
        await CreateAccountAsync();
        var json = await (await _factory.ClientFor(1).GetAsync("/api/employees")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("v1.", json);
    }

    [Fact]
    public async Task HeaderIdentity_IsDisabledByDefault()
    {
        using var strict = new AppFactory(new() { ["Auth:AllowHeaderIdentity"] = "false" });
        Assert.Equal(HttpStatusCode.Unauthorized, (await strict.ClientFor(1).GetAsync("/api/notes")).StatusCode);
    }

    [Fact]
    public async Task DemoQuickSwitch_IsDisabledByDefault()
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/demo/info")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/demo/login/1", null)).StatusCode);
    }

    [Fact]
    public async Task DemoQuickSwitch_WhenEnabled_SignsInWithoutPassword()
    {
        using var demo = new AppFactory(new() { ["Auth:AllowHeaderIdentity"] = "false", ["Demo:QuickSwitch"] = "true", ["Demo:Password"] = "Example1234" });
        var client = demo.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/demo/info")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/demo/login/3", null)).StatusCode);
        var me = await (await client.GetAsync("/api/auth/me")).ReadAsync<Employee>();
        Assert.Equal(3, me.EmployeeId);
    }

    [Fact]
    public async Task DemoSeeder_GivesSeededAccountsThePasswordWhenEnabled()
    {
        using var demo = new AppFactory(new() { ["Auth:AllowHeaderIdentity"] = "false", ["Demo:SeedPasswords"] = "true", ["Demo:Password"] = "Example1234" });
        var response = await demo.CreateClient().PostJsonAsync("/api/auth/login", new { email = "admin@notes.com", password = "Example1234" });
        Assert.Equal("Admin", (await response.ReadAsync<Employee>()).Role);
    }
}

public class RoleTests : IDisposable
{
    private readonly AppFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task OnlyAdmins_CanCreateEmployees()
    {
        var body = new { name = "Frank", email = "frank@example.com", password = "FrankPass123" };
        Assert.Equal(HttpStatusCode.Forbidden, (await _factory.ClientFor(2).PostJsonAsync("/api/employees", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _factory.ClientFor(1).PostJsonAsync("/api/employees", body)).StatusCode);
    }

    [Fact]
    public async Task OnlyAdmins_CanEditOrDeleteTopics()
    {
        var bob = _factory.ClientFor(2);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PutJsonAsync("/api/topics/3", new { name = "Renamed" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.DeleteAsync("/api/topics/3")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await bob.PostJsonAsync("/api/topics", new { name = "Anyone can add" })).StatusCode);
    }

    [Fact]
    public async Task TeamMembership_CanBeManagedByLeadsAndAdminsOnly()
    {
        // Seed: Alice (admin) leads Engineering; Bob is a plain Engineering member; Carol leads Marketing.
        Assert.Equal(HttpStatusCode.Forbidden, (await _factory.ClientFor(2).PostJsonAsync("/api/teams/1/members", new { employeeId = 4 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _factory.ClientFor(2).DeleteAsync("/api/teams/1/members/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _factory.ClientFor(3).PostJsonAsync("/api/teams/1/members", new { employeeId = 4 })).StatusCode);   // lead of a different team

        Assert.Equal(HttpStatusCode.Created, (await _factory.ClientFor(3).PostJsonAsync("/api/teams/2/members", new { employeeId = 4 })).StatusCode);       // lead
        Assert.Equal(HttpStatusCode.Created, (await _factory.ClientFor(1).PostJsonAsync("/api/teams/1/members", new { employeeId = 4 })).StatusCode);       // admin
    }

    [Fact]
    public async Task TeamCreator_BecomesLead()
    {
        var bob = _factory.ClientFor(2);
        var team = await (await bob.PostJsonAsync("/api/teams", new { name = "Support" })).ReadAsync<Team>(HttpStatusCode.Created);
        Assert.Equal("Lead", team.Members.Single().Role);
        Assert.Equal(HttpStatusCode.Created, (await bob.PostJsonAsync($"/api/teams/{team.TeamId}/members", new { employeeId = 4 })).StatusCode);
    }

    [Fact]
    public async Task Role_IsExposedOnEmployees()
    {
        var alice = await (await _factory.ClientFor(2).GetAsync("/api/employees/1")).ReadAsync<Employee>();
        Assert.Equal("Admin", alice.Role);
    }
}
