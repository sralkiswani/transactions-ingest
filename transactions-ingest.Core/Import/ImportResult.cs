using System.Diagnostics.CodeAnalysis;
using transactions_ingest.Core.Domain;

namespace transactions_ingest.Core.Import;

/// <summary>Outcome of <see cref="ImportService.RunAsync"/>: either another import held the lock, or a run summary.</summary>
public sealed class ImportResult
{
    public static readonly ImportResult AlreadyRunning = new(null);

    private ImportResult(ImportRunSummary? summary) => Summary = summary;

    public static ImportResult Completed(ImportRunSummary summary) => new(summary);

    public ImportRunSummary? Summary { get; }

    [MemberNotNullWhen(false, nameof(Summary))]
    public bool IsAlreadyRunning => Summary is null;
}

public sealed record ImportRunSummary(
    int RunId,
    ImportRunStatus Status,
    DateTime StartedAt,
    DateTime CompletedAt,
    int Finalized,
    IReadOnlyList<SourceSummary> Sources);

public sealed record SourceSummary(
    string SourceSystem,
    ImportSourceStatus Status,
    string? Error,
    int Fetched,
    int Inserted,
    int Updated,
    int Revoked,
    int Reinstated,
    int Rejected,
    int Ignored,
    bool RevocationSkipped)
{
    public static SourceSummary From(ImportRunSource s) =>
        new(s.SourceSystem, s.Status, s.Error, s.Fetched, s.Inserted, s.Updated, s.Revoked, s.Reinstated,
            s.Rejected, s.Ignored, s.RevocationSkipped);
}
