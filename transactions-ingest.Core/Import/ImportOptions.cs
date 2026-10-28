using System.ComponentModel.DataAnnotations;

namespace transactions_ingest.Core.Import;

/// <summary>Import settings bound from the <c>Import</c> configuration section.</summary>
public sealed class ImportOptions
{
    public const string SectionName = "Import";

    /// <summary>How far after the run's start a TransactionTime may be before it is rejected (D2).</summary>
    [Range(typeof(TimeSpan), "00:00:00", "01:00:00")]
    public TimeSpan FutureTolerance { get; set; } = TimeSpan.FromMinutes(5);

    public RevocationGuardOptions RevocationGuard { get; set; } = new();
}

/// <summary>Stops a source from revoking a suspicious share of its rows in one run (D4).</summary>
public sealed class RevocationGuardOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Below this many Active in-window rows the percentage check does not apply.</summary>
    [Range(0, int.MaxValue)]
    public int MinCandidates { get; set; } = 10;

    /// <summary>The guard trips when strictly more than this percentage of candidates would be revoked.</summary>
    [Range(0, 100)]
    public int MaxRevokedPercent { get; set; } = 50;
}
