namespace transactions_ingest.Core.Import;

/// <summary>
/// Refuses a plan that would revoke a suspicious share of a source's rows (D4). Checked before anything is
/// applied; a trip fails the whole source for this run.
/// </summary>
public static class RevocationGuard
{
    public static bool Trips(ReconciliationPlan plan, RevocationGuardOptions options) =>
        Trips(plan.RevocationCandidates, plan.Revocations.Count, options);

    /// <param name="candidates">Active in-window rows of the source before this run.</param>
    /// <param name="revocations">How many of them the plan would revoke.</param>
    public static bool Trips(int candidates, int revocations, RevocationGuardOptions options)
    {
        if (!options.Enabled || candidates == 0 || candidates < options.MinCandidates)
        {
            return false;
        }

        // Strictly more than the percentage; integer math avoids rounding at the boundary.
        return (long)revocations * 100 > (long)options.MaxRevokedPercent * candidates;
    }
}
