using Microsoft.EntityFrameworkCore;
using transactions_ingest.Core.Domain;

namespace transactions_ingest.Tests.Persistence;

public class DateTimeStorageTests
{
    private static readonly DateTime UtcTime = new(2026, 10, 28, 9, 30, 15, 123, DateTimeKind.Utc);

    [Fact]
    public void DateTime_comes_back_as_utc_with_the_same_value()
    {
        using var db = new SqliteTestDatabase();
        using (var context = db.CreateContext())
        {
            var transaction = SqliteTestDatabase.NewTransaction(transactionTime: UtcTime);
            transaction.FinalizedAt = UtcTime.AddHours(24);
            context.Transactions.Add(transaction);
            context.SaveChanges();
        }

        using (var context = db.CreateContext())
        {
            var stored = context.Transactions.Single();

            Assert.Equal(DateTimeKind.Utc, stored.TransactionTime.Kind);
            Assert.Equal(UtcTime, stored.TransactionTime);

            Assert.NotNull(stored.FinalizedAt);
            Assert.Equal(DateTimeKind.Utc, stored.FinalizedAt.Value.Kind);
            Assert.Equal(UtcTime.AddHours(24), stored.FinalizedAt.Value);
        }
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Writing_a_non_utc_DateTime_throws(DateTimeKind kind)
    {
        using var db = new SqliteTestDatabase();
        using var context = db.CreateContext();
        context.Transactions.Add(
            SqliteTestDatabase.NewTransaction(transactionTime: DateTime.SpecifyKind(UtcTime, kind)));

        AssertRejectedAsNonUtc(() => context.SaveChanges());
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Writing_a_non_utc_nullable_DateTime_throws(DateTimeKind kind)
    {
        using var db = new SqliteTestDatabase();
        using var context = db.CreateContext();
        context.ImportRuns.Add(new ImportRun
        {
            StartedAt = UtcTime,
            CompletedAt = DateTime.SpecifyKind(UtcTime, kind),
            Status = ImportRunStatus.Succeeded,
        });

        AssertRejectedAsNonUtc(() => context.SaveChanges());
    }

    private static void AssertRejectedAsNonUtc(Action save)
    {
        // EF wraps exceptions thrown by value converters during SaveChanges.
        var ex = Assert.Throws<DbUpdateException>(save);
        var inner = Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Contains("must be UTC", inner.Message);
    }
}
