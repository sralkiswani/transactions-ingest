using Microsoft.Extensions.Hosting;
using transactions_ingest.Core.Persistence;

namespace transactions_ingest.Core.Import;

/// <summary>
/// Fails app start when the registered sources can't be imported safely: a duplicate SourceSystem would
/// break the unique (ImportRunId, SourceSystem) index mid-run and mix two sources' keys.
/// </summary>
public sealed class SourceRegistrationCheck(IEnumerable<ITransactionSource> sources) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        Validate(sources);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static void Validate(IEnumerable<ITransactionSource> sources)
    {
        var names = sources.Select(s => s.SourceSystem).ToList();
        var problems = new List<string>();

        problems.AddRange(names
            .Where(n => string.IsNullOrWhiteSpace(n) || n.Length > TransactionsDbContext.MaxFieldLength)
            .Select(n => $"Source name '{n}' must be 1-{TransactionsDbContext.MaxFieldLength} characters."));
        problems.AddRange(names
            .GroupBy(n => n, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Count()} sources share the SourceSystem name '{g.Key}'."));

        if (problems.Count > 0)
        {
            throw new InvalidOperationException("Invalid transaction sources: " + string.Join(" ", problems));
        }
    }
}
