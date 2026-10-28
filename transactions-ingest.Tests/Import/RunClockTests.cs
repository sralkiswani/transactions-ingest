using transactions_ingest.Core.Import;
using static transactions_ingest.Tests.Import.ImportTestData;

namespace transactions_ingest.Tests.Import;

public class RunClockTests
{
    [Fact]
    public void Cutoff_and_future_limit_are_derived_from_now()
    {
        var clock = Clock();

        Assert.Equal(Now, clock.Now);
        Assert.Equal(Now.AddHours(-24), clock.Cutoff);
        Assert.Equal(Now.AddMinutes(5), clock.FutureLimit);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Non_utc_now_is_rejected(DateTimeKind kind)
    {
        Assert.Throws<ArgumentException>(() => new RunClock(DateTime.SpecifyKind(Now, kind), TimeSpan.Zero));
    }

    [Fact]
    public void Row_exactly_24h_old_is_locked_and_one_tick_younger_is_mutable()
    {
        var clock = Clock();

        Assert.False(clock.IsMutable(Now.AddHours(-24)));
        Assert.True(clock.IsMutable(Now.AddHours(-24).AddTicks(1)));
    }

    [Fact]
    public void Future_dated_row_is_mutable()
    {
        Assert.True(Clock().IsMutable(Now.AddMinutes(3)));
    }

    [Fact]
    public void Start_reads_the_time_provider_and_the_configured_tolerance()
    {
        var clock = RunClock.Start(new FixedTimeProvider(Now), new ImportOptions { FutureTolerance = TimeSpan.FromMinutes(2) });

        Assert.Equal(Now, clock.Now);
        Assert.Equal(DateTimeKind.Utc, clock.Now.Kind);
        Assert.Equal(Now.AddMinutes(2), clock.FutureLimit);
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
