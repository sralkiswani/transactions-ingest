using transactions_ingest.Core.Domain;
using static transactions_ingest.Tests.Import.ImportTestData;

namespace transactions_ingest.Tests.Import.Service;

/// <summary>
/// The 24h edge through the SQL queries: the window clause that loads rows and the finalization query
/// compare the TEXT column against a cutoff computed in C#. Times have fractional seconds.
/// </summary>
public class ImportServiceBoundaryTests
{
    private static readonly DateTime AtCutoff = Now.AddHours(-24);
    private static readonly DateTime JustInside = AtCutoff.AddTicks(1);

    [Fact]
    public async Task Missing_rows_either_side_of_the_cutoff()
    {
        using var h = new ImportHarness();
        h.Seed(Existing(1, TransactionStatus.Active, AtCutoff), Existing(2, TransactionStatus.Active, JustInside));
        h.AddSource(Source);

        var summary = await h.RunAsync();

        // At the cutoff: locked, so finalized rather than revoked.
        Assert.Equal(TransactionStatus.Finalized, h.Row(1).Status);
        // One tick inside: loaded by the window clause alone (not in the snapshot), revoked, not finalized.
        Assert.Equal(TransactionStatus.Revoked, h.Row(2).Status);
        Assert.Equal(1, summary.Finalized);
        Assert.Equal(1, Assert.Single(summary.Sources).Revoked);
    }

    [Fact]
    public async Task Changes_either_side_of_the_cutoff()
    {
        using var h = new ImportHarness();
        h.Seed(Existing(1, TransactionStatus.Active, AtCutoff), Existing(2, TransactionStatus.Active, JustInside));
        h.AddSource(Source, Raw(1, AtCutoff, amount: "99.00"), Raw(2, JustInside, amount: "99.00"));

        var summary = await h.RunAsync();

        Assert.Equal(10.00m, h.Row(1).Amount);
        Assert.Equal(TransactionStatus.Finalized, h.Row(1).Status);
        Assert.Equal(99.00m, h.Row(2).Amount);
        Assert.Equal(TransactionStatus.Active, h.Row(2).Status);
        var source = Assert.Single(summary.Sources);
        Assert.Equal(1, source.Ignored);
        Assert.Equal(1, source.Updated);
    }
}
