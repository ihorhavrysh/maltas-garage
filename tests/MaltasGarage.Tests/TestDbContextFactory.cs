using MaltasGarage.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Tests;

/// <summary>
/// Creates a fresh in-memory ApplicationDbContext for each test.
/// Each call returns an isolated database (unique name), so tests do not interfere.
/// </summary>
public static class TestDbContextFactory
{
    public static ApplicationDbContext Create() => Create(Guid.NewGuid().ToString());

    /// <summary>
    /// A relational (SQLite in-memory) context for tests that need real transactions, which the
    /// InMemory provider does not have. Contexts on the same open connection share one database.
    /// </summary>
    public static ApplicationDbContext CreateRelational(Microsoft.Data.Sqlite.SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new ApplicationDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    /// <summary>
    /// Contexts created with the same name share one store, which lets a test simulate two
    /// workers (say a background sweep and a page request) racing on the same data.
    /// </summary>
    public static ApplicationDbContext Create(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        var context = new ApplicationDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }
}
