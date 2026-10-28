using transactions_ingest.Core.Domain;
using transactions_ingest.Core.Persistence;

namespace transactions_ingest.Core.Import;

/// <summary>
/// Applies a <see cref="ReconciliationPlan"/> to tracked entities and writes the audit trail (D3, D6).
/// Does not save; the caller saves rows, audits and the source result together.
/// </summary>
public static class PlanApplier
{
    /// <param name="db">The context the plan's existing rows were loaded into.</param>
    public static void Apply(TransactionsDbContext db, string sourceSystem, ReconciliationPlan plan, RunClock clock, int importRunId)
    {
        var now = clock.Now;

        foreach (var incoming in plan.Inserts)
        {
            var row = new Transaction
            {
                SourceSystem = sourceSystem,
                TransactionId = incoming.TransactionId,
                CardNumber = incoming.CardNumber,
                LocationCode = incoming.LocationCode,
                ProductName = incoming.ProductName,
                Amount = incoming.Amount,
                TransactionTime = incoming.TransactionTime,
                Status = TransactionStatus.Active,
                CreatedAt = now,
                UpdatedAt = now,
            };
            row.Audits.Add(NewAudit(AuditChangeType.Created, importRunId, now));
            db.Transactions.Add(row);
        }

        foreach (var change in plan.Updates)
        {
            CopyFields(change, now, importRunId);
        }

        foreach (var change in plan.Reinstatements)
        {
            change.Row.Status = TransactionStatus.Active;
            change.Row.RevokedAt = null;
            change.Row.Audits.Add(NewAudit(AuditChangeType.Reinstated, importRunId, now));
            CopyFields(change, now, importRunId);
        }

        foreach (var row in plan.Revocations)
        {
            row.Status = TransactionStatus.Revoked;
            row.RevokedAt = now;
            row.UpdatedAt = now;
            row.Audits.Add(NewAudit(AuditChangeType.Revoked, importRunId, now));
        }
    }

    internal static TransactionAudit NewAudit(
        AuditChangeType changeType, int importRunId, DateTime changedAt, FieldChange? field = null) =>
        new()
        {
            ImportRunId = importRunId,
            ChangedAt = changedAt,
            ChangeType = changeType,
            FieldName = field?.FieldName,
            OldValue = field?.OldValue,
            NewValue = field?.NewValue,
        };

    private static void CopyFields(RowChange change, DateTime now, int importRunId)
    {
        var row = change.Row;
        var incoming = change.Incoming;

        row.CardNumber = incoming.CardNumber;
        row.LocationCode = incoming.LocationCode;
        row.ProductName = incoming.ProductName;
        row.Amount = incoming.Amount;
        row.TransactionTime = incoming.TransactionTime;
        row.UpdatedAt = now;

        foreach (var field in change.Changes)
        {
            row.Audits.Add(NewAudit(AuditChangeType.Updated, importRunId, now, field));
        }
    }
}
