namespace transactions_ingest.Core.Domain;

/// <summary>
/// A card transaction as last seen from its source. Keyed by (SourceSystem, TransactionId);
/// <see cref="Id"/> is a surrogate used for FKs and routes (D6).
/// </summary>
public class Transaction
{
    public int Id { get; set; }

    public required string SourceSystem { get; set; }

    /// <summary>The source's own ID; only unique within <see cref="SourceSystem"/>.</summary>
    public int TransactionId { get; set; }

    /// <summary>Masked last 4 digits as received (e.g. "***1111"), or null when the source sent none.</summary>
    public string? CardNumber { get; set; }

    public required string LocationCode { get; set; }

    public required string ProductName { get; set; }

    public decimal Amount { get; set; }

    public DateTime TransactionTime { get; set; }

    public TransactionStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? FinalizedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public List<TransactionAudit> Audits { get; set; } = [];
}
