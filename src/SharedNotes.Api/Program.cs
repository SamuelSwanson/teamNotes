using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SharedNotes.Api.Data;
using SharedNotes.Api.Identity;
using SharedNotes.Api.Middleware;
using SharedNotes.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddScoped<CurrentEmployeeAccessor>();
builder.Services.AddScoped<NoteService>();
builder.Services.AddScoped<TeamService>();
builder.Services.AddScoped<EmployeeService>();
builder.Services.AddScoped<TopicService>();

// The connection string is resolved lazily so integration tests can override it.
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var cs = sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")
             ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
    options.UseSqlite(ResolveConnectionString(cs, env.ContentRootPath));
});

var app = builder.Build();

// Apply migrations (creates the database and seed data on first run).
await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseMiddleware<EmployeeIdentityMiddleware>();
app.MapControllers();
app.Run();

// A relative database path is placed next to the project so the file is the same wherever `dotnet run` is invoked from.
static string ResolveConnectionString(string cs, string contentRoot)
{
    var csb = new SqliteConnectionStringBuilder(cs);
    var ds = csb.DataSource;
    if (!ds.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && ds != ":memory:" && !Path.IsPathRooted(ds))
        csb.DataSource = Path.Combine(contentRoot, ds);
    return csb.ToString();
}

public partial class Program;
