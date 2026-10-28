using transactions_ingest.Core.Domain;
using static transactions_ingest.Tests.Import.ImportTestData;

namespace transactions_ingest.Tests.Import;

/// <summary>
/// The window edge: a row exactly 24h old is locked, one tick younger is mutable (D3). <see cref="ImportTestData.Now"/>
/// has a fractional second, so these values are not whole seconds.
/// </summary>
public class TwentyFourHourBoundaryTests
{
    private static readonly DateTime AtCutoff = Now.AddHours(-24);
    private static readonly DateTime JustInside = AtCutoff.AddTicks(1);

    [Fact]
    public void Missing_row_at_exactly_24h_is_not_revoked()
    {
        var plan = Plan(Snapshot(), Existing(1, TransactionStatus.Active, AtCutoff));

        Assert.Empty(plan.Revocations);
        Assert.Equal(0, plan.RevocationCandidates);
    }

    [Fact]
    public void Missing_row_one_tick_inside_the_window_is_revoked()
    {
        var plan = Plan(Snapshot(), Existing(1, TransactionStatus.Active, JustInside));

        Assert.Single(plan.Revocations);
        Assert.Equal(1, plan.RevocationCandidates);
    }

    [Fact]
    public void Change_to_row_at_exactly_24h_is_ignored()
    {
        var plan = Plan(Snapshot(Raw(1, AtCutoff, amount: "99.00")), Existing(1, TransactionStatus.Active, AtCutoff));

        Assert.Empty(plan.Updates);
        Assert.Equal(1, plan.Ignored);
    }

    [Fact]
    public void Change_to_row_one_tick_inside_the_window_is_applied()
    {
        var plan = Plan(Snapshot(Raw(1, JustInside, amount: "99.00")), Existing(1, TransactionStatus.Active, JustInside));

        Assert.Single(plan.Updates);
        Assert.Equal(0, plan.Ignored);
    }

    [Fact]
    public void Revoked_row_at_exactly_24h_that_reappears_is_not_reinstated()
    {
        var plan = Plan(Snapshot(Raw(1, AtCutoff)), Existing(1, TransactionStatus.Revoked, AtCutoff));

        Assert.Empty(plan.Reinstatements);
    }

    [Fact]
    public void Revoked_row_one_tick_inside_the_window_that_reappears_is_reinstated()
    {
        var plan = Plan(Snapshot(Raw(1, JustInside)), Existing(1, TransactionStatus.Revoked, JustInside));

        Assert.Single(plan.Reinstatements);
    }
}
