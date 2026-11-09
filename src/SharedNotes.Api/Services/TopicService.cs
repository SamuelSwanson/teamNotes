using Microsoft.EntityFrameworkCore;
using SharedNotes.Api.Data;
using SharedNotes.Api.DTOs;
using SharedNotes.Api.Identity;
using SharedNotes.Api.Middleware;
using SharedNotes.Api.Models;

namespace SharedNotes.Api.Services;

public class TopicService(AppDbContext db, CurrentEmployeeAccessor current)
{
    public Task<List<TopicDto>> ListAsync() => db.Topics.AsNoTracking().OrderBy(t => t.Name).Select(ToDto).ToListAsync();

    public async Task<TopicDto> GetAsync(int id) =>
        await db.Topics.AsNoTracking().Where(t => t.TopicId == id).Select(ToDto).FirstOrDefaultAsync()
        ?? throw ApiException.NotFound($"Topic {id} not found.");

    public async Task<TopicDto> CreateAsync(TopicRequest r)
    {
        var name = r.Name.Trim();
        if (await db.Topics.AnyAsync(t => t.Name == name)) throw ApiException.Conflict($"A topic named '{name}' already exists.");

        var topic = new Topic { Name = name, Description = r.Description?.Trim() };
        db.Topics.Add(topic);
        await db.SaveChangesAsync();
        return await GetAsync(topic.TopicId);
    }

    public async Task<TopicDto> UpdateAsync(int id, TopicRequest r)
    {
        current.RequireAdmin();
        var topic = await db.Topics.FindAsync(id) ?? throw ApiException.NotFound($"Topic {id} not found.");
        var name = r.Name.Trim();
        if (await db.Topics.AnyAsync(t => t.Name == name && t.TopicId != id))
            throw ApiException.Conflict($"A topic named '{name}' already exists.");

        topic.Name = name;
        topic.Description = r.Description?.Trim();
        await db.SaveChangesAsync();
        return await GetAsync(id);
    }

    /// <summary>Deletes the topic and its note links (database cascade). The notes themselves are kept.</summary>
    public async Task DeleteAsync(int id)
    {
        current.RequireAdmin();
        var topic = await db.Topics.FindAsync(id) ?? throw ApiException.NotFound($"Topic {id} not found.");
        db.Topics.Remove(topic);
        await db.SaveChangesAsync();
    }

    private static readonly System.Linq.Expressions.Expression<Func<Topic, TopicDto>> ToDto =
        t => new TopicDto { TopicId = t.TopicId, Name = t.Name, Description = t.Description };
}
