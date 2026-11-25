using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProbeDesk.Core.Model;

namespace ProbeDesk.Storage;

public sealed record EnvironmentSummary(Guid Id, string Name, bool IsProduction);

public sealed class EnvironmentStore(IDbContextFactory<ProbeDeskDbContext> factory, ISecretProtector protector)
{
    public async Task<List<EnvironmentSummary>> ListAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Environments.AsNoTracking().OrderBy(e => e.Name)
            .Select(e => new EnvironmentSummary(e.Id, e.Name, e.IsProduction)).ToListAsync();
    }

    public async Task<RequestEnvironment?> GetAsync(Guid id)
    {
        await using var db = await factory.CreateDbContextAsync();
        var e = await db.Environments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return e is null ? null : Load(e);
    }

    /// <summary>
    /// Saves an environment. A secret variable with an empty value keeps its previously stored value,
    /// because secrets are write-only in the UI.
    /// </summary>
    public async Task SaveAsync(RequestEnvironment env)
    {
        await using var db = await factory.CreateDbContextAsync();
        var e = await db.Environments.FirstOrDefaultAsync(x => x.Id == env.Id);
        if (e is null) { e = new EnvironmentEntity { Id = env.Id }; db.Environments.Add(e); }
        else
        {
            var old = Load(e).Variables.GroupBy(v => v.Name).ToDictionary(g => g.Key, g => g.First().Value);
            foreach (var v in env.Variables.Where(v => v.IsSecret && v.Value.Length == 0 && old.ContainsKey(v.Name)))
                v.Value = old[v.Name];
        }
        e.Name = env.Name;
        e.IsProduction = env.IsProduction;
        e.Payload = protector.Protect(JsonSerializer.Serialize(env, Json.Options));
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Environments.Where(e => e.Id == id).ExecuteDeleteAsync();
    }

    private RequestEnvironment Load(EnvironmentEntity e)
    {
        var env = JsonSerializer.Deserialize<RequestEnvironment>(protector.Unprotect(e.Payload), Json.Options) ?? new();
        env.Id = e.Id; env.Name = e.Name; env.IsProduction = e.IsProduction;
        return env;
    }
}
