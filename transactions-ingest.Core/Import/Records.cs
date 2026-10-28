namespace transactions_ingest.Core.Import;

/// <summary>
/// A record exactly as a source delivered it. Every field is text so that parsing and validation happen in
/// one place (<see cref="TransactionValidator"/>), whatever the source's wire format.
/// </summary>
public sealed record RawTransaction(
    string? TransactionId,
    string? CardNumber,
    string? LocationCode,
    string? ProductName,
    string? Amount,
    string? TransactionTime,
    string? SourceSystem = null);

/// <summary>A record that passed validation. SourceSystem is implied by the source it came from.</summary>
public sealed record ParsedTransaction(
    int TransactionId,
    string? CardNumber,
    string LocationCode,
    string ProductName,
    decimal Amount,
    DateTime TransactionTime);

public abstract record ValidationOutcome;

public sealed record ValidRecord(ParsedTransaction Transaction) : ValidationOutcome;

/// <summary>The key parsed, so the key counts as present in the snapshot (D4), but the record is not used.</summary>
public sealed record RejectedRecord(int TransactionId, IReadOnlyList<string> Reasons) : ValidationOutcome;

/// <summary>The key itself could not be parsed, so the record cannot be matched to any row.</summary>
public sealed record UnparseableKey(string Reason) : ValidationOutcome;

/// <summary>Why a record was not used. <see cref="TransactionId"/> is null when the key did not parse.</summary>
public sealed record RecordRejection(int? TransactionId, IReadOnlyList<string> Reasons);
