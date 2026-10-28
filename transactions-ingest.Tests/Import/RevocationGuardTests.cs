using transactions_ingest.Core.Domain;
using transactions_ingest.Core.Import;
using static transactions_ingest.Tests.Import.ImportTestData;

namespace transactions_ingest.Tests.Import;

public class RevocationGuardTests
{
    private static readonly RevocationGuardOptions Defaults = new();

    [Theory]
    [InlineData(10, 6, true)]   // 60% > 50%
    [InlineData(10, 5, false)]  // exactly 50% is not more than 50%
    [InlineData(10, 10, true)]  // at the minimum, the percentage applies
    [InlineData(9, 9, false)]   // below the minimum
    [InlineData(0, 0, false)]
    [InlineData(100, 50, false)]
    [InlineData(100, 51, true)]
    public void Default_guard(int candidates, int revocations, bool trips)
    {
        Assert.Equal(trips, RevocationGuard.Trips(candidates, revocations, Defaults));
    }

    [Fact]
    public void Disabled_guard_never_trips()
    {
        Assert.False(RevocationGuard.Trips(10, 10, new RevocationGuardOptions { Enabled = false }));
    }

    [Fact]
    public void Defaults_are_enabled_with_10_candidates_and_50_percent()
    {
        Assert.True(Defaults.Enabled);
        Assert.Equal(10, Defaults.MinCandidates);
        Assert.Equal(50, Defaults.MaxRevokedPercent);
    }

    [Fact]
    public void Empty_snapshot_with_10_candidates_trips()
    {
        var plan = Plan(Snapshot(), ActiveRows(10));

        Assert.Equal(10, plan.Revocations.Count);
        Assert.True(RevocationGuard.Trips(plan, Defaults));
    }

    [Fact]
    public void Empty_snapshot_with_9_candidates_passes_and_revokes_them_all()
    {
        var plan = Plan(Snapshot(), ActiveRows(9));

        Assert.Equal(9, plan.Revocations.Count);
        Assert.False(RevocationGuard.Trips(plan, Defaults));
    }

    [Fact]
    public void Skipped_revocation_never_trips()
    {
        var plan = Plan(Snapshot(Raw(id: "x")), ActiveRows(10));

        Assert.True(plan.RevocationSkipped);
        Assert.False(RevocationGuard.Trips(plan, Defaults));
    }

    private static Transaction[] ActiveRows(int count) =>
        Enumerable.Range(1, count).Select(id => Existing(id)).ToArray();
}
