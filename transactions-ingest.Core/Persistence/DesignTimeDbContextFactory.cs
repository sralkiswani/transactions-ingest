using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace transactions_ingest.Core.Persistence;

/// <summary>
/// Lets `dotnet ef` build the context from this project alone. Only used by tooling; the connection
/// string is never opened when adding migrations.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TransactionsDbContext>
{
    public TransactionsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<TransactionsDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options);
}
