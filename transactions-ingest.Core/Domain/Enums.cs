namespace transactions_ingest.Core.Domain;

public enum TransactionStatus
{
    Active,
    Revoked,
    Finalized,
}

public enum AuditChangeType
{
    Created,
    Updated,
    Revoked,
    Reinstated,
    Finalized,
}

public enum ImportRunStatus
{
    Running,
    Succeeded,
    PartiallyFailed,
    Failed,
}

public enum ImportSourceStatus
{
    Succeeded,
    Failed,
}
