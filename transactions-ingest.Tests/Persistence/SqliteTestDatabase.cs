using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using transactions_ingest.Core.Domain;
using transactions_ingest.Core.Persistence;

namespace transactions_ingest.Tests.Persistence;

/// <summary>
/// An in-memory SQLite database migrated with the real migrations. The connection stays open for the
/// lifetime of the instance, since closing it drops the database.
/// </summary>
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteTestDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using var context = CreateContext();
        context.Database.Migrate();
    }

    public SqliteConnection Connection => _connection;

    /// <summary>A fresh context with an empty change tracker, so reads hit the database.</summary>
    public TransactionsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TransactionsDbContext>()
            .UseSqlite(_connection)
            .Options);

    public static Transaction NewTransaction(
        string sourceSystem = "Uptown",
        int transactionId = 1,
        decimal amount = 10.00m,
        DateTime? transactionTime = null)
    {
        var now = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        return new Transaction
        {
            SourceSystem = sourceSystem,
            TransactionId = transactionId,
            CardNumber = "***1111",
            LocationCode = "LOC-1",
            ProductName = "Coffee",
            Amount = amount,
            TransactionTime = transactionTime ?? now,
            Status = TransactionStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Dispose() => _connection.Dispose();
}
