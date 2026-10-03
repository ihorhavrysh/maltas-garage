using MaltasGarage.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MaltasGarage.Tests.Services;

public class UtcDateTimeTests
{
    [Fact]
    public async Task DatesReadFromTheDatabase_AreUtc()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        connection.Open();
        var stamp = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);

        using (var write = TestDbContextFactory.CreateRelational(connection))
        {
            write.Categories.Add(new Category { Id = Guid.NewGuid(), Name = "Sports", Slug = "sports", CreatedAt = stamp });
            await write.SaveChangesAsync();
        }

        using var read = TestDbContextFactory.CreateRelational(connection);
        var category = await read.Categories.SingleAsync();

        Assert.Equal(DateTimeKind.Utc, category.CreatedAt.Kind);
        Assert.Equal(stamp, category.CreatedAt);
    }
}
