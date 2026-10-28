namespace transactions_ingest.Core.Domain;

/// <summary>Outcome of one source within an import run.</summary>
public class ImportRunSource
{
    public int Id { get; set; }

    public int ImportRunId { get; set; }

    public ImportRun ImportRun { get; set; } = null!;

    public required string SourceSystem { get; set; }

    public ImportSourceStatus Status { get; set; }

    public string? Error { get; set; }

    public int Fetched { get; set; }

    public int Inserted { get; set; }

    public int Updated { get; set; }

    public int Revoked { get; set; }

    public int Reinstated { get; set; }

    public int Rejected { get; set; }

    public int Ignored { get; set; }

    /// <summary>True when revocation was skipped for this source (unparseable or duplicate keys, D4).</summary>
    public bool RevocationSkipped { get; set; }
}
