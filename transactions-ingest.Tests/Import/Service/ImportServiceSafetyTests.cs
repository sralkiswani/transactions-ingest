using Microsoft.Extensions.Logging;
using transactions_ingest.Core.Domain;
using static transactions_ingest.Tests.Import.ImportTestData;

namespace transactions_ingest.Tests.Import.Service;

/// <summary>D4 at service level: failed, partial or ambiguous snapshots never look like removals.</summary>
public class ImportServiceSafetyTests
{
    private const string Other = "Online";

    [Fact]
    public async Task Source_that_throws_fails_alone_and_revokes_nothing()
    {
        using var h = new ImportHarness();
        var online = Existing(5);
        online.SourceSystem = Other;
        h.Seed(Existing(1), online);
        h.AddSource(Source).Throws = new InvalidOperationException("source down");
        h.AddSource(Other, Raw(id: "5", amount: "11.00"));

        var summary = await h.RunAsync();

        Assert.Equal(TransactionStatus.Active, h.Row(1).Status);
        Assert.Empty(h.Audits(1));
        Assert.Equal(11.00m, h.Row(5, Other).Amount);
        Assert.Equal(ImportRunStatus.PartiallyFailed, summary.Status);
        var failed = summary.Sources.Single(s => s.SourceSystem == Source);
        Assert.Equal(ImportSourceStatus.Failed, failed.Status);
        Assert.Equal("source down", failed.Error);
        Assert.Equal(ImportSourceStatus.Succeeded, summary.Sources.Single(s => s.SourceSystem == Other).Status);
    }

    [Fact]
    public async Task Failure_while_saving_rolls_back_that_source_only()
    {
        using var h = new ImportHarness(new FailSavingSource(Source));
        h.Seed(Existing(2));
        h.AddSource(Source, Raw(id: "1"));
        h.AddSource(Other, Raw(id: "1"));

        var summary = await h.RunAsync();

        Assert.DoesNotContain(h.Rows(), t => t.SourceSystem == Source && t.TransactionId == 1);
        Assert.Equal(TransactionStatus.Active, h.Row(2).Status);
        Assert.Empty(h.Audits(2));
        Assert.Equal(TransactionStatus.Active, h.Row(1, Other).Status);
        var failed = summary.Sources.Single(s => s.SourceSystem == Source);
        Assert.Equal(ImportSourceStatus.Failed, failed.Status);
        Assert.Equal("Simulated failure while saving.", failed.Error);
        Assert.Equal(ImportSourceStatus.Failed, h.Runs().Single().Sources.Single(s => s.SourceSystem == Source).Status);
        Assert.Equal(ImportRunStatus.PartiallyFailed, summary.Status);
    }

    [Fact]
    public async Task Rejected_record_leaves_its_row_unchanged_and_not_revoked()
    {
        using var h = new ImportHarness();
        h.Seed(Existing(1));
        h.AddSource(Source, Raw(id: "1", amount: "bad"));

        var summary = await h.RunAsync();

        Assert.Equal(TransactionStatus.Active, h.Row(1).Status);
        Assert.Empty(h.Audits());
        var source = Assert.Single(summary.Sources);
        Assert.Equal(1, source.Rejected);
        Assert.Equal(0, source.Revoked);
    }

    [Fact]
    public async Task Unparseable_key_applies_valid_updates_but_skips_revocation()
    {
        using var h = new ImportHarness();
        h.Seed(Existing(1), Existing(2));
        h.AddSource(Source, Raw(id: "1", amount: "11.00"), Raw(id: "x"));

        var summary = await h.RunAsync();

        Assert.Equal(11.00m, h.Row(1).Amount);
        Assert.Equal(TransactionStatus.Active, h.Row(2).Status);
        Assert.True(Assert.Single(summary.Sources).RevocationSkipped);
        Assert.Contains(h.Logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("revocation skipped"));
    }

    [Fact]
    public async Task Duplicate_key_leaves_its_row_unchanged_and_skips_revocation()
    {
        using var h = new ImportHarness();
        h.Seed(Existing(1), Existing(2));
        h.AddSource(Source, Raw(id: "1", amount: "11.00"), Raw(id: "1", amount: "12.00"));

        var summary = await h.RunAsync();

        Assert.Equal(10.00m, h.Row(1).Amount);
        Assert.Equal(TransactionStatus.Active, h.Row(2).Status);
        var source = Assert.Single(summary.Sources);
        Assert.True(source.RevocationSkipped);
        Assert.Equal(2, source.Rejected);
    }

    [Fact]
    public async Task Tripped_guard_fails_the_source_with_no_changes_at_all()
    {
        using var h = new ImportHarness();
        h.Seed(TenActiveRows());
        h.AddSource(Source, Raw(id: "1"), Raw(id: "2"), Raw(id: "3"), Raw(id: "4"), Raw(id: "99"));

        var summary = await h.RunAsync();

        var source = Assert.Single(summary.Sources);
        Assert.Equal(ImportSourceStatus.Failed, source.Status);
        Assert.Contains("Revocation guard", source.Error);
        Assert.All(h.Rows(), t => Assert.Equal(TransactionStatus.Active, t.Status));
        Assert.DoesNotContain(h.Rows(), t => t.TransactionId == 99);
        Assert.Empty(h.Audits());
        Assert.Equal(ImportRunStatus.Failed, summary.Status);
    }

    [Fact]
    public async Task Disabled_guard_lets_the_same_snapshot_through()
    {
        using var h = new ImportHarness();
        h.Options.RevocationGuard.Enabled = false;
        h.Seed(TenActiveRows());
        h.AddSource(Source, Raw(id: "1"), Raw(id: "2"), Raw(id: "3"), Raw(id: "4"), Raw(id: "99"));

        var summary = await h.RunAsync();

        var source = Assert.Single(summary.Sources);
        Assert.Equal(ImportSourceStatus.Succeeded, source.Status);
        Assert.Equal(6, source.Revoked);
        Assert.Equal(1, source.Inserted);
    }

    private static Transaction[] TenActiveRows() =>
        Enumerable.Range(1, 10).Select(id => Existing(id)).ToArray();
}
