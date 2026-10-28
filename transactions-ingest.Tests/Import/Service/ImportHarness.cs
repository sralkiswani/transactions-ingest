using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using transactions_ingest.Core.Domain;
using transactions_ingest.Core.Import;
using transactions_ingest.Core.Persistence;
using transactions_ingest.Tests.Persistence;

namespace transactions_ingest.Tests.Import.Service;

/// <summary>
/// An <see cref="ImportService"/> over in-memory SQLite (real migrations), fake sources, a controllable
/// clock and a capturing logger.
/// </summary>
internal sealed class ImportHarness : IDisposable
{
    public ImportHarness(params IInterceptor[] interceptors)
    {
        Factory = new TestDbContextFactory(Db, interceptors);
    }

    public SqliteTestDatabase Db { get; } = new();

    public TestDbContextFactory Factory { get; }

    public TestTimeProvider Time { get; } = new(ImportTestData.Now);

    public List<FakeSource> Sources { get; } = [];

    public IImportLock Lock { get; } = new InMemoryImportLock();

    public ImportOptions Options { get; } = new();

    public ListLogger<ImportService> Logger { get; } = new();

    public FakeSource AddSource(string name = ImportTestData.Source, params RawTransaction[] records)
    {
        var source = new FakeSource(name) { Records = [.. records] };
        Sources.Add(source);
        return source;
    }

    public ImportService CreateService() =>
        new(Factory, Sources, Lock, Time, Microsoft.Extensions.Options.Options.Create(Options), Logger);

    /// <summary>Runs one import and returns its summary; fails the test if the lock was held.</summary>
    public async Task<ImportRunSummary> RunAsync()
    {
        var result = await CreateService().RunAsync(TestContext.Current.CancellationToken);
        Assert.False(result.IsAlreadyRunning, "The import lock was unexpectedly held.");
        return result.Summary;
    }

    public void Seed(params Transaction[] rows)
    {
        using var db = Db.CreateContext();
        foreach (var row in rows)
        {
            row.Id = 0; // let SQLite assign, so rows from different sources never collide
        }
        db.Transactions.AddRange(rows);
        db.SaveChanges();
    }

    public Transaction Row(int transactionId, string source = ImportTestData.Source)
    {
        using var db = Db.CreateContext();
        return db.Transactions.Single(t => t.SourceSystem == source && t.TransactionId == transactionId);
    }

    public List<Transaction> Rows()
    {
        using var db = Db.CreateContext();
        return db.Transactions.OrderBy(t => t.SourceSystem).ThenBy(t => t.TransactionId).ToList();
    }

    /// <summary>Every column of every row, for "nothing changed" comparisons.</summary>
    public List<string> RowState() =>
        Rows().Select(t => string.Join("|",
            t.Id, t.SourceSystem, t.TransactionId, t.CardNumber, t.LocationCode, t.ProductName, t.Amount,
            t.TransactionTime.ToString("O"), t.Status, t.CreatedAt.ToString("O"), t.UpdatedAt.ToString("O"),
            t.FinalizedAt?.ToString("O"), t.RevokedAt?.ToString("O"))).ToList();

    public List<TransactionAudit> Audits(int? transactionId = null, string source = ImportTestData.Source)
    {
        using var db = Db.CreateContext();
        return db.TransactionAudits
            .Include(a => a.Transaction)
            .Where(a => transactionId == null || (a.Transaction.SourceSystem == source && a.Transaction.TransactionId == transactionId))
            .OrderBy(a => a.Id)
            .ToList();
    }

    public List<ImportRun> Runs()
    {
        using var db = Db.CreateContext();
        return db.ImportRuns.Include(r => r.Sources).OrderBy(r => r.Id).ToList();
    }

    public int WarningsMentioning(string text) =>
        Logger.Entries.Count(e => e.Level == LogLevel.Warning && e.Message.Contains(text, StringComparison.Ordinal));

    public void Dispose() => Db.Dispose();
}

internal sealed class TestDbContextFactory(SqliteTestDatabase db, IInterceptor[] interceptors) : IDbContextFactory<TransactionsDbContext>
{
    public int Created { get; private set; }

    /// <summary>When set, creating the context with this 1-based number throws, simulating a database failure.</summary>
    public int? FailOnCreate { get; set; }

    public TransactionsDbContext CreateDbContext()
    {
        Created++;
        if (Created == FailOnCreate)
        {
            throw new InvalidOperationException("Simulated database failure.");
        }
        return db.CreateContext(interceptors);
    }
}

internal sealed class TestTimeProvider(DateTime utcNow) : TimeProvider
{
    private DateTimeOffset _now = new(utcNow);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

internal sealed class FakeSource(string sourceSystem) : ITransactionSource
{
    public string SourceSystem => sourceSystem;

    public List<RawTransaction> Records { get; set; } = [];

    public Exception? Throws { get; set; }

    /// <summary>Runs inside FetchAsync before the result is returned; lets a test block or move the clock.</summary>
    public Func<CancellationToken, Task>? OnFetch { get; set; }

    public async Task<IReadOnlyList<RawTransaction>> FetchAsync(CancellationToken cancellationToken)
    {
        if (OnFetch is not null)
        {
            await OnFetch(cancellationToken);
        }
        if (Throws is not null)
        {
            throw Throws;
        }
        return Records.ToList();
    }
}

internal sealed class ListLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (Entries)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}

/// <summary>Throws when a successful result for the given source is being saved, i.e. mid-apply.</summary>
internal sealed class FailSavingSource(string sourceSystem) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var failing = eventData.Context!.ChangeTracker.Entries<ImportRunSource>().Any(e =>
            e.State == EntityState.Added
            && e.Entity.SourceSystem == sourceSystem
            && e.Entity.Status == ImportSourceStatus.Succeeded);
        if (failing)
        {
            throw new InvalidOperationException("Simulated failure while saving.");
        }
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
