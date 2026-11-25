using Microsoft.EntityFrameworkCore;

namespace ProbeDesk.Storage;

public class FolderEntity
{
    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = "";
}

public class RequestEntity
{
    public Guid Id { get; set; }
    public Guid? FolderId { get; set; }
    public string Name { get; set; } = "";
    public string Method { get; set; } = "GET";
    /// <summary>Protected JSON of the full saved request (may contain secrets).</summary>
    public string Payload { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class EnvironmentEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsProduction { get; set; }
    /// <summary>Protected JSON of the environment including secret variable values.</summary>
    public string Payload { get; set; } = "";
}

public class HistoryEntity
{
    public Guid Id { get; set; }
    public DateTime TimeUtc { get; set; }
    public string Method { get; set; } = "";
    public string Host { get; set; } = "";
    public string Path { get; set; } = "";
    public int Status { get; set; }
    public long DurationMs { get; set; }
}

public class ProbeDeskDbContext(DbContextOptions<ProbeDeskDbContext> options) : DbContext(options)
{
    public DbSet<FolderEntity> Folders => Set<FolderEntity>();
    public DbSet<RequestEntity> Requests => Set<RequestEntity>();
    public DbSet<EnvironmentEntity> Environments => Set<EnvironmentEntity>();
    public DbSet<HistoryEntity> History => Set<HistoryEntity>();
}
