using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProbeDesk.Core.Model;

namespace ProbeDesk.Storage;

public sealed record RequestSummary(Guid Id, Guid? FolderId, string Name, string Method);

public sealed class CollectionStore(IDbContextFactory<ProbeDeskDbContext> factory, ISecretProtector protector)
{
    private sealed class Payload
    {
        public Core.Http.RequestSpec Spec { get; set; } = new();
        public List<Guid> RunBefore { get; set; } = [];
        public List<Extractor> Extractors { get; set; } = [];
        public List<Assertion> Assertions { get; set; } = [];
    }

    public async Task<List<Folder>> ListFoldersAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return (await db.Folders.AsNoTracking().OrderBy(f => f.Name).ToListAsync())
            .Select(f => new Folder { Id = f.Id, ParentId = f.ParentId, Name = f.Name }).ToList();
    }

    public async Task<List<RequestSummary>> ListRequestsAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return (await db.Requests.AsNoTracking().OrderBy(r => r.CreatedAt).ToListAsync())
            .Select(r => new RequestSummary(r.Id, r.FolderId, r.Name, r.Method)).ToList();
    }

    public async Task<List<RequestSummary>> SearchAsync(string query)
    {
        var all = await ListRequestsAsync();
        if (string.IsNullOrWhiteSpace(query)) return all;
        return all.Where(r => r.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                              || r.Method.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public async Task<SavedRequest?> GetRequestAsync(Guid id)
    {
        await using var db = await factory.CreateDbContextAsync();
        var e = await db.Requests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        return e is null ? null : ToModel(e);
    }

    public async Task SaveRequestAsync(SavedRequest r)
    {
        await using var db = await factory.CreateDbContextAsync();
        var e = await db.Requests.FirstOrDefaultAsync(x => x.Id == r.Id);
        if (e is null)
        {
            e = new RequestEntity { Id = r.Id, CreatedAt = DateTime.UtcNow };
            db.Requests.Add(e);
        }
        e.FolderId = r.FolderId;
        e.Name = r.Name;
        e.Method = r.Spec.Method;
        e.UpdatedAt = DateTime.UtcNow;
        e.Payload = protector.Protect(JsonSerializer.Serialize(
            new Payload { Spec = r.Spec, RunBefore = r.RunBefore, Extractors = r.Extractors, Assertions = r.Assertions }, Json.Options));
        await db.SaveChangesAsync();
    }

    public async Task RenameRequestAsync(Guid id, string name)
    {
        await using var db = await factory.CreateDbContextAsync();
        var e = await db.Requests.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return;
        e.Name = name; e.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<SavedRequest?> DuplicateRequestAsync(Guid id)
    {
        var r = await GetRequestAsync(id);
        if (r is null) return null;
        r.Id = Guid.NewGuid();
        r.Name += " (copy)";
        await SaveRequestAsync(r);
        return r;
    }

    public async Task MoveRequestAsync(Guid id, Guid? folderId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var e = await db.Requests.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return;
        e.FolderId = folderId;
        await db.SaveChangesAsync();
    }

    public async Task DeleteRequestAsync(Guid id)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Requests.Where(r => r.Id == id).ExecuteDeleteAsync();
    }

    public async Task<Folder> CreateFolderAsync(string name, Guid? parentId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var f = new FolderEntity { Id = Guid.NewGuid(), ParentId = parentId, Name = name };
        db.Folders.Add(f);
        await db.SaveChangesAsync();
        return new Folder { Id = f.Id, ParentId = parentId, Name = name };
    }

    public async Task RenameFolderAsync(Guid id, string name)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Folders.Where(f => f.Id == id).ExecuteUpdateAsync(s => s.SetProperty(f => f.Name, name));
    }

    /// <summary>Deletes a folder with all subfolders and requests.</summary>
    public async Task DeleteFolderAsync(Guid id)
    {
        await using var db = await factory.CreateDbContextAsync();
        var folders = await db.Folders.AsNoTracking().ToListAsync();
        var doomed = new HashSet<Guid> { id };
        var grew = true;
        while (grew)
        {
            grew = false;
            foreach (var f in folders.Where(f => f.ParentId is { } p && doomed.Contains(p) && !doomed.Contains(f.Id)))
            { doomed.Add(f.Id); grew = true; }
        }
        await db.Requests.Where(r => r.FolderId != null && doomed.Contains(r.FolderId.Value)).ExecuteDeleteAsync();
        await db.Folders.Where(f => doomed.Contains(f.Id)).ExecuteDeleteAsync();
    }

    /// <summary>All requests in a folder and its subfolders, in creation order.</summary>
    public async Task<List<SavedRequest>> GetRequestsInFolderAsync(Guid? folderId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var folders = await db.Folders.AsNoTracking().ToListAsync();
        var scope = new HashSet<Guid?> { folderId };
        var grew = true;
        while (grew)
        {
            grew = false;
            foreach (var f in folders.Where(f => scope.Contains(f.ParentId) && !scope.Contains(f.Id)))
            { scope.Add(f.Id); grew = true; }
        }
        var entities = await db.Requests.AsNoTracking().OrderBy(r => r.CreatedAt).ToListAsync();
        var result = new List<SavedRequest>();
        foreach (var r in entities.Where(r => folderId is null || scope.Contains(r.FolderId)))
        {
            // One unreadable row (for example after a profile change) must not break export or cleanup.
            try { result.Add(ToModel(r)); }
            catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException or FormatException) { }
        }
        return result;
    }

    public async Task<FolderNode> ExportTreeAsync(Guid? folderId = null)
    {
        var folders = await ListFoldersAsync();
        var requests = await GetRequestsInFolderAsync(folderId);
        var name = folders.FirstOrDefault(f => f.Id == folderId)?.Name ?? "Blueberry Probe";
        FolderNode Build(Guid? id, string n) => new()
        {
            Name = n,
            Folders = folders.Where(f => f.ParentId == id).Select(f => Build(f.Id, f.Name)).ToList(),
            Requests = requests.Where(r => r.FolderId == id).ToList()
        };
        return Build(folderId, name);
    }

    /// <summary>Stores an imported tree. The root node's own name is not created as a folder.</summary>
    public async Task ImportTreeAsync(FolderNode root, Guid? parentFolderId = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        void Walk(FolderNode node, Guid? parent)
        {
            foreach (var r in node.Requests)
            {
                r.Id = Guid.NewGuid();
                r.FolderId = parent;
                db.Requests.Add(new RequestEntity
                {
                    Id = r.Id, FolderId = parent, Name = r.Name, Method = r.Spec.Method, CreatedAt = now, UpdatedAt = now,
                    Payload = protector.Protect(JsonSerializer.Serialize(
                        new Payload { Spec = r.Spec, RunBefore = r.RunBefore, Extractors = r.Extractors, Assertions = r.Assertions }, Json.Options))
                });
            }
            foreach (var child in node.Folders)
            {
                var f = new FolderEntity { Id = Guid.NewGuid(), ParentId = parent, Name = child.Name };
                db.Folders.Add(f);
                Walk(child, f.Id);
            }
        }
        Walk(root, parentFolderId);
        // A single SaveChanges is one transaction: a failure imports nothing.
        await db.SaveChangesAsync();
    }

    public async Task<HashSet<string>> GetReferencedFilePathsAsync()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in await GetRequestsInFolderAsync(null))
        {
            if (r.Spec.BinaryFilePath.Length > 0) paths.Add(r.Spec.BinaryFilePath);
            foreach (var f in r.Spec.FormFields.Where(f => f.IsFile && f.FilePath.Length > 0)) paths.Add(f.FilePath);
        }
        return paths;
    }

    public async Task<bool> IsEmptyAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return !await db.Folders.AnyAsync() && !await db.Requests.AnyAsync();
    }

    private SavedRequest ToModel(RequestEntity e)
    {
        var p = JsonSerializer.Deserialize<Payload>(protector.Unprotect(e.Payload), Json.Options) ?? new Payload();
        return new SavedRequest
        {
            Id = e.Id, FolderId = e.FolderId, Name = e.Name, Spec = p.Spec, RunBefore = p.RunBefore,
            Extractors = p.Extractors, Assertions = p.Assertions, UpdatedAt = e.UpdatedAt
        };
    }
}
