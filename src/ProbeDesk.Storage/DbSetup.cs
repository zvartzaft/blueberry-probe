using Microsoft.EntityFrameworkCore;

namespace ProbeDesk.Storage;

public static class DbSetup
{
    private sealed class Factory(DbContextOptions<ProbeDeskDbContext> options) : IDbContextFactory<ProbeDeskDbContext>
    {
        public ProbeDeskDbContext CreateDbContext() => new(options);
    }

    public static DbContextOptions<ProbeDeskDbContext> Options(string dbPath) =>
        new DbContextOptionsBuilder<ProbeDeskDbContext>().UseSqlite($"Data Source={dbPath}").Options;

    public static IDbContextFactory<ProbeDeskDbContext> CreateFactory(string dbPath)
    {
        var factory = new Factory(Options(dbPath));
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
        return factory;
    }
}
