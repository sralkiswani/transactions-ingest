using transactions_ingest.Core.Domain;
using transactions_ingest.Core.Import;
using static transactions_ingest.Tests.Import.ImportTestData;

namespace transactions_ingest.Tests.Import.Service;

/// <summary>
/// D5: re-running with the same snapshots changes nothing. The clock moves an hour between runs (rows stay
/// in the window), so a stray timestamp write would show up as a difference.
/// </summary>
public class ImportServiceIdempotencyTests
{
    [Fact]
    public async Task Second_run_with_identical_snapshots_changes_nothing()
    {
        using var h = new ImportHarness();
        h.Seed(Existing(4));
        h.AddSource(Source, Raw(id: "1"), Raw(id: "2", amount: "-5.25"), Raw(id: "3", card: ""), Raw(id: "4", amount: "12.00"));
        await h.RunAsync();
        var rows = h.RowState();
        var audits = h.Audits().Count;

        h.Time.Advance(TimeSpan.FromHours(1));
        var second = await h.RunAsync();

        Assert.Equal(rows, h.RowState());
        Assert.Equal(audits, h.Audits().Count);
        AssertAllZero(Assert.Single(second.Sources));
        Assert.Equal(0, second.Finalized);
    }

    [Fact]
    public async Task Rerun_after_a_revocation_changes_nothing()
    {
        using var h = new ImportHarness();
        h.Seed(Existing(1), Existing(2));
        h.AddSource(Source, Raw(id: "1"));
        await h.RunAsync();
        Assert.Equal(TransactionStatus.Revoked, h.Row(2).Status);
        var rows = h.RowState();
        var audits = h.Audits().Count;

        h.Time.Advance(TimeSpan.FromHours(1));
        var second = await h.RunAsync();

        Assert.Equal(rows, h.RowState());
        Assert.Equal(audits, h.Audits().Count);
        AssertAllZero(Assert.Single(second.Sources));
    }

    [Fact]
    public async Task Rerun_after_finalization_changes_nothing()
    {
        using var h = new ImportHarness();
        h.AddSource(Source, Raw(1, PastWindow));
        await h.RunAsync();
        Assert.Equal(TransactionStatus.Finalized, h.Row(1).Status);
        var rows = h.RowState();
        var audits = h.Audits().Count;

        h.Time.Advance(TimeSpan.FromHours(1));
        var second = await h.RunAsync();

        Assert.Equal(rows, h.RowState());
        Assert.Equal(audits, h.Audits().Count);
        AssertAllZero(Assert.Single(second.Sources));
        Assert.Equal(0, second.Finalized);
    }

    [Fact]
    public async Task Formatting_only_differences_between_runs_change_nothing()
    {
        using var h = new ImportHarness();
        var source = h.AddSource(Source, Raw(id: "1", amount: "10.00", card: "", time: Iso(InWindow)));
        await h.RunAsync();
        var rows = h.RowState();
        var audits = h.Audits().Count;

        source.Records = [Raw(id: "1", amount: "10.0", card: null, time: Iso(InWindow).TrimEnd('Z') + "+00:00")];
        h.Time.Advance(TimeSpan.FromHours(1));
        var second = await h.RunAsync();

        Assert.Equal(rows, h.RowState());
        Assert.Equal(audits, h.Audits().Count);
        AssertAllZero(Assert.Single(second.Sources));
    }

    private static void AssertAllZero(SourceSummary s)
    {
        Assert.Equal(ImportSourceStatus.Succeeded, s.Status);
        Assert.Equal((0, 0, 0, 0, 0, 0), (s.Inserted, s.Updated, s.Revoked, s.Reinstated, s.Rejected, s.Ignored));
    }
}
