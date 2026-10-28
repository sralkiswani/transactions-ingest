using System.ComponentModel.DataAnnotations;
using transactions_ingest.Core.Domain;
using transactions_ingest.Core.Persistence;

namespace transactions_ingest.Server.Contracts;

/// <summary>Query string for GET /api/transactions. Invalid values return 400 ProblemDetails.</summary>
public sealed class TransactionListQuery
{
    public const int MaxPageSize = 100;

    [Range(1, 1_000_000)]
    public int Page { get; set; } = 1;

    [Range(1, MaxPageSize)]
    public int PageSize { get; set; } = 25;

    [StringLength(TransactionsDbContext.MaxFieldLength, MinimumLength = 1)]
    public string? Source { get; set; }

    // Rejects numeric values that bind to an undefined enum member, e.g. status=7.
    [EnumDataType(typeof(TransactionStatus))]
    public TransactionStatus? Status { get; set; }
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

/// <summary>One row of the transactions table. <see cref="Id"/> addresses the audit view.</summary>
public sealed record TransactionDto(
    int Id,
    string SourceSystem,
    int TransactionId,
    string LocationCode,
    string ProductName,
    decimal Amount,
    DateTime TransactionTime,
    TransactionStatus Status);

public sealed record AuditEntryDto(
    DateTime ChangedAt,
    AuditChangeType ChangeType,
    string? FieldName,
    string? OldValue,
    string? NewValue,
    int ImportRunId);
