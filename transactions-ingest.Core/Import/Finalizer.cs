using Microsoft.EntityFrameworkCore;
using transactions_ingest.Core.Domain;
using transactions_ingest.Core.Persistence;

namespace transactions_ingest.Core.Import;

/// <summary>
/// Locks Active rows whose window has closed (D3). Purely time-based, so it runs after every import
/// whatever happened to the sources. Revoked rows are left Revoked.
/// </summary>
public static class Finalizer
{
    /// <returns>How many rows were finalized.</returns>
    public static async Task<int> FinalizeAsync(TransactionsDbContext db, RunClock clock, int importRunId, CancellationToken cancellationToken)
    {
        // The cutoff is computed in C#; the column is compared against it as-is (same operator as RunClock.IsMutable).
        var cutoff = clock.Cutoff;
        var rows = await db.Transactions
            .Where(t => t.Status == TransactionStatus.Active && t.TransactionTime <= cutoff)
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            row.Status = TransactionStatus.Finalized;
            row.FinalizedAt = clock.Now;
            row.UpdatedAt = clock.Now;
            row.Audits.Add(PlanApplier.NewAudit(AuditChangeType.Finalized, importRunId, clock.Now));
        }

        await db.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }
}
