namespace transactions_ingest.Core.Import;

/// <summary>
/// The single time reference for one import run (D3). Created once at the start of a run and passed to
/// every step, so the 24h boundary cannot move while a run is in progress.
/// </summary>
public sealed class RunClock
{
    /// <summary>How long after TransactionTime a row can still change. Fixed by the ticket, not config.</summary>
    public static readonly TimeSpan MutableWindow = TimeSpan.FromHours(24);

    public RunClock(DateTime now, TimeSpan futureTolerance)
    {
        if (now.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException($"Run time must be UTC; got Kind={now.Kind}.", nameof(now));
        }
        ArgumentOutOfRangeException.ThrowIfLessThan(futureTolerance, TimeSpan.Zero);

        Now = now;
        Cutoff = now - MutableWindow;
        FutureLimit = now + futureTolerance;
    }

    public static RunClock Start(TimeProvider timeProvider, ImportOptions options) =>
        new(timeProvider.GetUtcNow().UtcDateTime, options.FutureTolerance);

    public DateTime Now { get; }

    /// <summary>
    /// Rows with <c>TransactionTime &gt; Cutoff</c> are mutable. SQL queries compare the column against this
    /// value with the same operator, so C# and the database agree at the boundary.
    /// </summary>
    public DateTime Cutoff { get; }

    /// <summary>Incoming TransactionTime values after this are rejected (D2).</summary>
    public DateTime FutureLimit { get; }

    /// <summary>Equivalent to <c>Now &lt; transactionTime + 24h</c>.</summary>
    public bool IsMutable(DateTime transactionTime) => transactionTime > Cutoff;
}
