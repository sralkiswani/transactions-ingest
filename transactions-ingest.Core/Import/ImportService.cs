using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using transactions_ingest.Core.Domain;
using transactions_ingest.Core.Persistence;

namespace transactions_ingest.Core.Import;

/// <summary>
/// Runs one import across all sources (see docs/import-plan.md). Safe to call from the API and from a
/// scheduled job: the lock lives here, not in the caller (D8).
/// </summary>
public sealed class ImportService(
    IDbContextFactory<TransactionsDbContext> dbFactory,
    IEnumerable<ITransactionSource> sources,
    IImportLock importLock,
    TimeProvider timeProvider,
    IOptions<ImportOptions> options,
    ILogger<ImportService> logger)
{
    public async Task<ImportResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var handle = importLock.TryAcquire();
        if (handle is null)
        {
            logger.LogInformation("Import not started: another import is already running.");
            return ImportResult.AlreadyRunning;
        }

        try
        {
            return ImportResult.Completed(await RunLockedAsync(cancellationToken));
        }
        finally
        {
            handle.Dispose();
        }
    }

    private async Task<ImportRunSummary> RunLockedAsync(CancellationToken cancellationToken)
    {
        // Read the time once; every rule in this run uses this clock (D3).
        var clock = RunClock.Start(timeProvider, options.Value);
        var runId = await StartRunAsync(clock, cancellationToken);

        try
        {
            var results = new List<ImportRunSource>();
            foreach (var source in sources)
            {
                results.Add(await ImportSourceAsync(source, runId, clock, cancellationToken));
            }

            int finalized;
            await using (var db = dbFactory.CreateDbContext())
            {
                finalized = await Finalizer.FinalizeAsync(db, clock, runId, cancellationToken);
            }

            var failed = results.Count(r => r.Status == ImportSourceStatus.Failed);
            var status = failed == 0 ? ImportRunStatus.Succeeded
                : failed == results.Count ? ImportRunStatus.Failed
                : ImportRunStatus.PartiallyFailed;

            var completedAt = await CompleteRunAsync(runId, status, cancellationToken);
            logger.LogInformation(
                "Import run {RunId} {Status}: {SourceCount} sources, {Failed} failed, {Finalized} finalized.",
                runId, status, results.Count, failed, finalized);

            return new ImportRunSummary(runId, status, clock.Now, completedAt, finalized, results.Select(SourceSummary.From).ToList());
        }
        finally
        {
            // An unexpected exception or cancellation must not leave the run Running forever.
            await MarkFailedIfStillRunningAsync(runId);
        }
    }

    private async Task<ImportRunSource> ImportSourceAsync(ITransactionSource source, int runId, RunClock clock, CancellationToken cancellationToken)
    {
        var name = source.SourceSystem;
        ParsedSnapshot? snapshot = null;
        try
        {
            // Fetched before any DB work; a source that throws fails here and never looks like an empty snapshot (D4).
            var records = await source.FetchAsync(cancellationToken);
            snapshot = SnapshotParser.Parse(name, records, clock);
            LogRejections(snapshot);

            // A fresh context per source: if this source fails, nothing it tracked leaks into the next one.
            await using var db = dbFactory.CreateDbContext();
            var cutoff = clock.Cutoff;
            var keys = snapshot.Valid.Keys.ToList();
            var existing = await db.Transactions
                .Where(t => t.SourceSystem == name && (t.TransactionTime > cutoff || keys.Contains(t.TransactionId)))
                .ToListAsync(cancellationToken);

            var plan = ReconciliationPlanner.Plan(snapshot, existing, clock);

            if (RevocationGuard.Trips(plan, options.Value.RevocationGuard))
            {
                var error = $"Revocation guard tripped: {plan.Revocations.Count} of {plan.RevocationCandidates} " +
                    $"active in-window rows would be revoked (limit {options.Value.RevocationGuard.MaxRevokedPercent}%). No changes applied.";
                logger.LogError("Source {Source}: {Error}", name, error);
                return await SaveFailureAsync(runId, name, snapshot, error, cancellationToken);
            }

            PlanApplier.Apply(db, name, plan, clock, runId);
            var result = new ImportRunSource
            {
                ImportRunId = runId,
                SourceSystem = name,
                Status = ImportSourceStatus.Succeeded,
                Fetched = snapshot.Fetched,
                Inserted = plan.Inserts.Count,
                Updated = plan.Updates.Count,
                Revoked = plan.Revocations.Count,
                Reinstated = plan.Reinstatements.Count,
                Rejected = snapshot.Rejected,
                Ignored = plan.Ignored,
                RevocationSkipped = plan.RevocationSkipped,
            };
            db.ImportRunSources.Add(result);

            // One SaveChanges = one DB transaction: rows, audits and the source result commit together (D8).
            await db.SaveChangesAsync(cancellationToken);

            if (plan.Ignored > 0)
            {
                logger.LogWarning("Source {Source}: ignored changes to {Ignored} locked rows.", name, plan.Ignored);
            }
            logger.LogInformation(
                "Source {Source}: {Fetched} fetched, {Inserted} inserted, {Updated} updated, {Revoked} revoked, " +
                "{Reinstated} reinstated, {Rejected} rejected, {Ignored} ignored.",
                name, result.Fetched, result.Inserted, result.Updated, result.Revoked, result.Reinstated, result.Rejected, result.Ignored);
            return result;
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            logger.LogError(ex, "Source {Source} failed; no changes applied for it in this run.", name);
            return await SaveFailureAsync(runId, name, snapshot, ex.Message, cancellationToken);
        }
    }

    private void LogRejections(ParsedSnapshot snapshot)
    {
        if (snapshot.Rejected > 0)
        {
            logger.LogWarning(
                "Source {Source}: {Rejected} of {Fetched} records rejected.", snapshot.SourceSystem, snapshot.Rejected, snapshot.Fetched);
            foreach (var rejection in snapshot.Rejections)
            {
                // Reasons never contain field values (D2), so this cannot leak card data.
                logger.LogDebug(
                    "Source {Source}: record {TransactionId} rejected: {Reasons}",
                    snapshot.SourceSystem, rejection.TransactionId, string.Join(" ", rejection.Reasons));
            }
        }
        if (!snapshot.RevocationAllowed)
        {
            logger.LogError(
                "Source {Source}: {Unparseable} unparseable and {Duplicates} duplicated keys; revocation skipped for this run.",
                snapshot.SourceSystem, snapshot.UnparseableKeys, snapshot.DuplicateKeys.Count);
        }
    }

    private async Task<int> StartRunAsync(RunClock clock, CancellationToken cancellationToken)
    {
        await using var db = dbFactory.CreateDbContext();
        var run = new ImportRun { StartedAt = clock.Now, Status = ImportRunStatus.Running };
        db.ImportRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        return run.Id;
    }

    /// <summary>Records a failed source in its own context, so none of the failed attempt's changes are saved.</summary>
    private async Task<ImportRunSource> SaveFailureAsync(
        int runId, string name, ParsedSnapshot? snapshot, string error, CancellationToken cancellationToken)
    {
        await using var db = dbFactory.CreateDbContext();
        var result = new ImportRunSource
        {
            ImportRunId = runId,
            SourceSystem = name,
            Status = ImportSourceStatus.Failed,
            Error = error,
            Fetched = snapshot?.Fetched ?? 0,
            Rejected = snapshot?.Rejected ?? 0,
            RevocationSkipped = snapshot is { RevocationAllowed: false },
        };
        db.ImportRunSources.Add(result);
        await db.SaveChangesAsync(cancellationToken);
        return result;
    }

    private async Task<DateTime> CompleteRunAsync(int runId, ImportRunStatus status, CancellationToken cancellationToken)
    {
        // CompletedAt is run bookkeeping, not a business timestamp, so it reads the real time.
        var completedAt = timeProvider.GetUtcNow().UtcDateTime;
        await using var db = dbFactory.CreateDbContext();
        await db.ImportRuns
            .Where(r => r.Id == runId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, status).SetProperty(r => r.CompletedAt, completedAt), cancellationToken);
        return completedAt;
    }

    private async Task MarkFailedIfStillRunningAsync(int runId)
    {
        try
        {
            var completedAt = timeProvider.GetUtcNow().UtcDateTime;
            await using var db = dbFactory.CreateDbContext();
            var marked = await db.ImportRuns
                .Where(r => r.Id == runId && r.Status == ImportRunStatus.Running)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ImportRunStatus.Failed).SetProperty(r => r.CompletedAt, completedAt));
            if (marked > 0)
            {
                logger.LogError("Import run {RunId} ended unexpectedly and was marked Failed.", runId);
            }
        }
        catch (Exception ex)
        {
            // Never hide the exception that ended the run.
            logger.LogError(ex, "Could not mark import run {RunId} as Failed.", runId);
        }
    }
}
