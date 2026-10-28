using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace transactions_ingest.Tests.Persistence;

public class TransactionKeyTests
{
    private const int SqliteConstraintError = 19;

    [Fact]
    public void Duplicate_source_and_transaction_id_is_rejected()
    {
        using var db = new SqliteTestDatabase();
        using (var context = db.CreateContext())
        {
            context.Transactions.Add(SqliteTestDatabase.NewTransaction("Uptown", 42));
            context.SaveChanges();
        }

        using (var context = db.CreateContext())
        {
            context.Transactions.Add(SqliteTestDatabase.NewTransaction("Uptown", 42));

            var ex = Assert.Throws<DbUpdateException>(() => context.SaveChanges());
            var sqlite = Assert.IsType<SqliteException>(ex.InnerException);
            Assert.Equal(SqliteConstraintError, sqlite.SqliteErrorCode);
        }
    }

    [Fact]
    public void Same_transaction_id_is_allowed_in_different_sources()
    {
        using var db = new SqliteTestDatabase();
        using (var context = db.CreateContext())
        {
            context.Transactions.Add(SqliteTestDatabase.NewTransaction("Uptown", 42));
            context.Transactions.Add(SqliteTestDatabase.NewTransaction("Online", 42));
            context.SaveChanges();
        }

        using (var context = db.CreateContext())
        {
            Assert.Equal(2, context.Transactions.Count(t => t.TransactionId == 42));
        }
    }
}
