using Microsoft.EntityFrameworkCore;

namespace transactions_ingest.Tests.Persistence;

public class MigrationTests
{
    [Fact]
    public void Migrations_apply_and_match_the_model()
    {
        using var db = new SqliteTestDatabase();
        using var context = db.CreateContext();

        Assert.NotEmpty(context.Database.GetAppliedMigrations());
        Assert.Empty(context.Database.GetPendingMigrations());
        Assert.False(context.Database.HasPendingModelChanges(),
            "The model has changes not captured in a migration. Run 'dotnet ef migrations add'.");
    }
}
