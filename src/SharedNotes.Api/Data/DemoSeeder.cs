using Microsoft.EntityFrameworkCore;
using SharedNotes.Api.Identity;

namespace SharedNotes.Api.Data;

/// <summary>
/// Gives the seeded demo accounts a known, made-up password. Only runs when <c>Demo:SeedPasswords</c> is true
/// (set in appsettings.Development.json). Accounts that already have a password are left untouched.
/// </summary>
public static class DemoSeeder
{
    public static async Task SeedPasswordsAsync(AppDbContext db, IConfiguration config)
    {
        if (!config.GetValue<bool>("Demo:SeedPasswords")) return;
        var password = config["Demo:Password"];
        if (string.IsNullOrWhiteSpace(password)) return;

        var accounts = await db.Employees.Where(e => e.PasswordHash == null).ToListAsync();
        if (accounts.Count == 0) return;
        foreach (var account in accounts) account.PasswordHash = PasswordHasher.Hash(password);
        await db.SaveChangesAsync();
    }
}
