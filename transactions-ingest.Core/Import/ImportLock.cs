namespace transactions_ingest.Core.Import;

/// <summary>Ensures only one import runs at a time (D8).</summary>
public interface IImportLock
{
    /// <summary>Returns a handle that releases the lock when disposed, or null if an import already holds it.</summary>
    IDisposable? TryAcquire();
}

/// <summary>
/// Process-wide lock; register as a singleton. Serializes every caller in this process, including a
/// scheduled job, and can't go stale after a crash. Does not coordinate across processes.
/// </summary>
public sealed class InMemoryImportLock : IImportLock
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public IDisposable? TryAcquire() => _semaphore.Wait(0) ? new Releaser(_semaphore) : null;

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}
