using transactions_ingest.Core.Domain;
using static transactions_ingest.Tests.Import.ImportTestData;

namespace transactions_ingest.Tests.Import.Service;

/// <summary>D3 at service level: rows, audits and counts as stored in SQLite.</summary>
public class ImportServiceReconciliationTests
{
    [Fact]
    public async Task New_key_is_inserted_as_active_with_a_created_audit()
    {
        using var h = new ImportHarness();
        h.AddSource(Source, Raw(id: "1"));

        var summary = await h.RunAsync();

        var row = h.Row(1);
        Assert.Equal(TransactionStatus.Active, row.Status);
        Assert.Equal(Now, row.CreatedAt);
        Assert.Equal(Now, row.UpdatedAt);
        var audit = Assert.Single(h.Audits(1));
        Assert.Equal(AuditChangeType.Created, audit.ChangeType);
        Assert.Equal(summary.RunId, audit.ImportRunId);
        Assert.Equal(ImportRunStatus.Succeeded, summary.Status);
        Assert.Equal(1, Assert.Single(summary.Sources).Inserted);
    }

    [Fact]
    public async Task Late_key_is_inserted_and_finalized_in_the_same_run()
    {
        using var h = new ImportHarness();
        h.AddSource(Source, Raw(1, PastWindow));

        var summary = await h.RunAsync();

        var row = h.Row(1);
        Assert.Equal(TransactionStatus.Finalized, row.Status);
        Assert.Equal(Now, row.FinalizedAt);
        Assert.Equal([AuditChangeType.Created, AuditChangeType.Finalized], h.Audits(1).Select(a => a.ChangeType));
        Assert.Equal(1, summary.Finalized);
    }

    [Fact]
    public async Task Changed_fields_are_updated_with_one_audit_per_field()
    {
        using var h = new ImportHarness();
        h.Seed(Existing(1));
        h.AddSource(Source, Raw(id: "1", location: "LOC-2", amount: "12.5"));

        var summary = await h.RunAsync();

        var row = h.Row(1);
        Assert.Equal("LOC-2", row.LocationCode);
        Assert.Equal(12.50m, row.Amount);
        Assert.Equal(Now, row.UpdatedAt);
        Assert.Equal(Now.AddDays(-2), row.CreatedAt);
        Assert.Equal(
            [("LocationCode", "LOC-1", "LOC-2"), ("Amount", "10.00", "12.50")],
            h.Audits(1).Select(a => (a.FieldName, a.OldValue, a.NewValue)));
        Assert.All(h.Audits(1), a => Assert.Equal(AuditChangeType.Updated, a.ChangeType));
        Assert.Equal(1, Assert.Single(summary.Sources).Updated);
    }

    [Fact]
    public async Task Changes_to_finalized_rows_are_ignored_with_one_warning_per_source()
    {
        using var h = new ImportHarness();
        h.Seed(Existing(1, TransactionStatus.Finalized, PastWindow), Existing(2, TransactionStatus.Finalized, PastWindow));
        h.AddSource(Source, Raw(1, PastWindow, amount: "99.00"), Raw(2, PastWindow, amount: "98.00"));

        var summary = await h.RunAsync();

        Assert.Equal(10.00m, h.Row(1).Amount);
        Assert.Equal(10.00m, h.Row(2).Amount);
        Assert.Empty(h.Audits());
        Assert.Equal(2, Assert.Single(summary.Sources).Ignored);
        Assert.Equal(1, h.WarningsMentioning("locked rows"));
    }

    [Fact]
    public async Task Change_to_active_row_past_the_window_is_ignored_and_the_row_finalized()
    {
        using var h = new ImportHarness();
        h.Seed(Existing(1, TransactionStatus.Active, PastWindow));
        h.AddSource(Source, Raw(1, PastWindow, amount: "99.00"));

        var summary = await h.RunAsync();

        var row = h.Row(1);
        Assert.Equal(TransactionStatus.Finalized, row.Status);
        Assert.Equal(10.00m, row.Amount);
        Assert.Equal(AuditChangeType.Finalized, Assert.Single(h.Audits(1)).ChangeType);
        Assert.Equal(1, Assert.Single(summary.Sources).Ignored);
    }

    [Fact]
    public async Task Missing_active_in_window_row_is_revoked()
    {
        using var h = new ImportHarness();
        h.Seed(Existing(1), Existing(2));
        h.AddSource(Source, Raw(id: "1"));

        var summary = await h.RunAsync();

        var row = h.Row(2);
        Assert.Equal(TransactionStatus.Revoked, row.Status);
        Assert.Equal(Now, row.RevokedAt);
        Assert.Equal(AuditChangeType.Revoked, Assert.Single(h.Audits(2)).ChangeType);
        Assert.Equal(1, Assert.Single(summary.Sources).Revoked);
        Assert.Equal(TransactionStatus.Active, h.Row(1).Status);
    }

    [Fact]
    public async Task Missing_active_row_past_the_window_is_finalized_not_revoked()
    {
        using var h = new ImportHarness();
        h.Seed(Existing(1, TransactionStatus.Active, PastWindow));
        h.AddSource(Source);

        await h.RunAsync();

        Assert.Equal(TransactionStatus.Finalized, h.Row(1).Status);
        Assert.Equal(AuditChangeType.Finalized, Assert.Single(h.Audits(1)).ChangeType);
    }

    [Fact]
    public async Task Revoked_row_still_missing_gets_no_new_audit()
    {
        using var h = new ImportHarness();
        h.Seed(Existing(1, TransactionStatus.Revoked));
        h.AddSource(Source);

        await h.RunAsync();

        Assert.Equal(TransactionStatus.Revoked, h.Row(1).Status);
        Assert.Empty(h.Audits());
    }

    [Fact]
    public async Task Revoked_in_window_row_that_reappears_is_reinstated_with_its_changes()
    {
        using var h = new ImportHarness();
        var revoked = Existing(1, TransactionStatus.Revoked);
        revoked.RevokedAt = Now.AddMinutes(-30);
        h.Seed(revoked);
        h.AddSource(Source, Raw(id: "1", amount: "11.00"));

        var summary = await h.RunAsync();

        var row = h.Row(1);
        Assert.Equal(TransactionStatus.Active, row.Status);
        Assert.Null(row.RevokedAt);
        Assert.Equal(11.00m, row.Amount);
        Assert.Equal(
            [(AuditChangeType.Reinstated, null), (AuditChangeType.Updated, "Amount")],
            h.Audits(1).Select(a => (a.ChangeType, a.FieldName)));
        var source = Assert.Single(summary.Sources);
        Assert.Equal(1, source.Reinstated);
        Assert.Equal(0, source.Updated);
    }

    [Fact]
    public async Task Revoked_row_past_the_window_that_reappears_stays_revoked_and_is_not_finalized()
    {
        using var h = new ImportHarness();
        h.Seed(Existing(1, TransactionStatus.Revoked, PastWindow));
        h.AddSource(Source, Raw(1, PastWindow, amount: "99.00"));

        var summary = await h.RunAsync();

        Assert.Equal(TransactionStatus.Revoked, h.Row(1).Status);
        Assert.Empty(h.Audits());
        Assert.Equal(1, Assert.Single(summary.Sources).Ignored);
    }

    [Fact]
    public async Task Revoked_row_whose_reappearing_record_is_invalid_stays_revoked()
    {
        using var h = new ImportHarness();
        h.Seed(Existing(1, TransactionStatus.Revoked));
        h.AddSource(Source, Raw(id: "1", amount: "bad"));

        var summary = await h.RunAsync();

        Assert.Equal(TransactionStatus.Revoked, h.Row(1).Status);
        Assert.Empty(h.Audits());
        Assert.Equal(1, Assert.Single(summary.Sources).Rejected);
    }

    [Fact]
    public async Task Clock_is_read_once_per_run()
    {
        using var h = new ImportHarness();
        var nearCutoff = Now.AddHours(-24).AddMinutes(30);
        h.Seed(Existing(2), Existing(3, TransactionStatus.Active, nearCutoff));
        h.AddSource(Source, Raw(id: "1"), Raw(3, nearCutoff)).OnFetch = _ =>
        {
            h.Time.Advance(TimeSpan.FromHours(2));
            return Task.CompletedTask;
        };

        var summary = await h.RunAsync();

        Assert.Equal(Now, summary.StartedAt);
        Assert.Equal(Now, h.Row(1).CreatedAt);
        Assert.Equal(Now, h.Row(2).RevokedAt);
        // With the advanced clock this row would be past the window and finalized.
        Assert.Equal(TransactionStatus.Active, h.Row(3).Status);
        Assert.All(h.Audits(), a => Assert.Equal(Now, a.ChangedAt));
        Assert.Equal(Now.AddHours(2), summary.CompletedAt);
    }

    [Fact]
    public async Task Finalizer_runs_even_when_every_source_failed()
    {
        using var h = new ImportHarness();
        h.Seed(Existing(1, TransactionStatus.Active, PastWindow));
        h.AddSource(Source).Throws = new InvalidOperationException("source down");

        var summary = await h.RunAsync();

        Assert.Equal(ImportRunStatus.Failed, summary.Status);
        Assert.Equal(1, summary.Finalized);
        Assert.Equal(TransactionStatus.Finalized, h.Row(1).Status);
    }
}
