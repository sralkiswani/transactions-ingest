namespace transactions_ingest.Core.Import;

/// <summary>One upstream system that publishes full snapshots of its recent transactions (D1).</summary>
public interface ITransactionSource
{
    /// <summary>The name stored as <c>SourceSystem</c>; part of every key from this source.</summary>
    string SourceSystem { get; }

    /// <summary>
    /// Returns the complete current snapshot. Must throw on any failure, never return a partial or empty
    /// list instead: an empty snapshot is a legitimate answer that revokes rows (D4).
    /// </summary>
    Task<IReadOnlyList<RawTransaction>> FetchAsync(CancellationToken cancellationToken);
}
