using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace transactions_ingest.Tests.Persistence;

public class AmountStorageTests
{
    [Theory]
    [InlineData("0.00")]
    [InlineData("0.01")]
    [InlineData("-0.01")]
    [InlineData("-42.50")]
    [InlineData("1234567.89")]
    [InlineData("9999999999999999.99")]
    [InlineData("-9999999999999999.99")]
    public void Amount_round_trips_exactly(string value)
    {
        var amount = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        using var db = new SqliteTestDatabase();

        using (var context = db.CreateContext())
        {
            context.Transactions.Add(SqliteTestDatabase.NewTransaction(amount: amount));
            context.SaveChanges();
        }

        using (var context = db.CreateContext())
        {
            Assert.Equal(amount, context.Transactions.Single().Amount);
        }
    }

    [Theory]
    [InlineData("18.5", "18.50")]
    [InlineData("25", "25.00")]
    [InlineData("-59.9", "-59.90")]
    [InlineData("0", "0.00")]
    public void Amount_reads_back_with_two_decimals(string written, string expected)
    {
        using var db = new SqliteTestDatabase();
        using (var context = db.CreateContext())
        {
            context.Transactions.Add(SqliteTestDatabase.NewTransaction(amount: decimal.Parse(written, System.Globalization.CultureInfo.InvariantCulture)));
            context.SaveChanges();
        }

        using (var context = db.CreateContext())
        {
            Assert.Equal(expected, context.Transactions.Single().Amount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public void Amount_is_stored_as_integer_cents()
    {
        using var db = new SqliteTestDatabase();
        using (var context = db.CreateContext())
        {
            context.Transactions.Add(SqliteTestDatabase.NewTransaction(amount: -42.50m));
            context.SaveChanges();
        }

        using var command = new SqliteCommand("SELECT typeof(Amount), Amount FROM Transactions", db.Connection);
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("integer", reader.GetString(0));
        Assert.Equal(-4250L, reader.GetInt64(1));
    }

    [Fact]
    public void Amount_sorts_and_filters_numerically_in_queries()
    {
        // As TEXT these would sort "-100.00" < "-5.50" < "10.00" < "100.00" < "2.25" < "9.99".
        decimal[] amounts = [10.00m, -5.50m, 2.25m, 100.00m, -100.00m, 9.99m];
        using var db = new SqliteTestDatabase();
        using (var context = db.CreateContext())
        {
            for (var i = 0; i < amounts.Length; i++)
            {
                context.Transactions.Add(SqliteTestDatabase.NewTransaction(transactionId: i + 1, amount: amounts[i]));
            }
            context.SaveChanges();
        }

        using (var context = db.CreateContext())
        {
            var ascending = context.Transactions.OrderBy(t => t.Amount).Select(t => t.Amount).ToList();
            Assert.Equal(amounts.Order(), ascending);

            var aboveFive = context.Transactions.Where(t => t.Amount > 5m).OrderBy(t => t.Amount).Select(t => t.Amount).ToList();
            Assert.Equal([9.99m, 10.00m, 100.00m], aboveFive);
        }
    }

    [Fact]
    public void Amount_with_more_than_two_decimals_is_rejected_on_write()
    {
        using var db = new SqliteTestDatabase();
        using var context = db.CreateContext();
        context.Transactions.Add(SqliteTestDatabase.NewTransaction(amount: 1.005m));

        // EF wraps exceptions thrown by value converters during SaveChanges.
        var ex = Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        var inner = Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Contains("more than 2 decimal places", inner.Message);
    }
}
