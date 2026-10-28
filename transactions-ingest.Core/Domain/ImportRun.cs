namespace transactions_ingest.Core.Domain;

public class ImportRun
{
    public int Id { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public ImportRunStatus Status { get; set; }

    public List<ImportRunSource> Sources { get; set; } = [];
}
