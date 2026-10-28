namespace transactions_ingest.Core.Domain;

/// <summary>
/// One change to a transaction: a whole-row event (Created, Revoked, ...) or a single field change (D6).
/// </summary>
public class TransactionAudit
{
    public int Id { get; set; }

    public int TransactionPk { get; set; }

    public Transaction Transaction { get; set; } = null!;

    public int ImportRunId { get; set; }

    public ImportRun ImportRun { get; set; } = null!;

    public DateTime ChangedAt { get; set; }

    public AuditChangeType ChangeType { get; set; }

    /// <summary>Null for whole-row events.</summary>
    public string? FieldName { get; set; }

    /// <summary>Invariant-culture text.</summary>
    public string? OldValue { get; set; }

    /// <summary>Invariant-culture text.</summary>
    public string? NewValue { get; set; }
}
