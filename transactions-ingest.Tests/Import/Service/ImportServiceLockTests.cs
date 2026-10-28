using transactions_ingest.Core.Domain;
using static transactions_ingest.Tests.Import.ImportTestData;

namespace transactions_ingest.Tests.Import.Service;

/// <summary>D8: one import at a time, and the lock and run status always end up released and final.</summary>
public class ImportServiceLockTests
{
    [Fact]
    public async Task Second_import_while_one_is_running_is_refused_across_service_instances()
    {
        using var h = new ImportHarness();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.AddSource(Source, Raw(id: "1")).OnFetch = async _ =>
        {
            entered.SetResult();
            await release.Task;
        };

        // Separate instances, as the endpoint and a scheduled job would get from different DI scopes.
        var first = h.CreateService().RunAsync(TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var second = await h.CreateService().RunAsync(TestContext.Current.CancellationToken);
        release.SetResult();
        var firstResult = await first;

        Assert.True(second.IsAlreadyRunning);
        Assert.False(firstResult.IsAlreadyRunning);
        Assert.Single(h.Runs());
    }

    [Fact]
    public async Task Lock_is_released_after_a_successful_run()
    {
        using var h = new ImportHarness();
        h.AddSource(Source, Raw(id: "1"));

        await h.RunAsync();
        await h.RunAsync();

        Assert.Equal(2, h.Runs().Count);
    }

    [Fact]
    public async Task Lock_is_released_after_a_source_failure()
    {
        using var h = new ImportHarness();
        var source = h.AddSource(Source, Raw(id: "1"));
        source.Throws = new InvalidOperationException("source down");
        await h.RunAsync();

        source.Throws = null;
        var second = await h.RunAsync();

        Assert.Equal(ImportRunStatus.Succeeded, second.Status);
    }

    [Fact]
    public async Task Unexpected_exception_marks_the_run_failed_and_releases_the_lock()
    {
        using var h = new ImportHarness();
        h.AddSource(Source, Raw(id: "1"));
        // Contexts: 1 = start run, 2 = the source, 3 = finalizer.
        h.Factory.FailOnCreate = 3;

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.CreateService().RunAsync(TestContext.Current.CancellationToken));

        var run = Assert.Single(h.Runs());
        Assert.Equal(ImportRunStatus.Failed, run.Status);
        Assert.NotNull(run.CompletedAt);

        h.Factory.FailOnCreate = null;
        var second = await h.RunAsync();
        Assert.Equal(ImportRunStatus.Succeeded, second.Status);
    }

    [Fact]
    public async Task Cancellation_marks_the_run_failed_and_releases_the_lock()
    {
        using var h = new ImportHarness();
        using var cts = new CancellationTokenSource();
        var source = h.AddSource(Source, Raw(id: "1"));
        source.OnFetch = ct =>
        {
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.CreateService().RunAsync(cts.Token));

        var run = Assert.Single(h.Runs());
        Assert.Equal(ImportRunStatus.Failed, run.Status);
        Assert.Empty(run.Sources);

        source.OnFetch = null;
        var second = await h.RunAsync();
        Assert.Equal(ImportRunStatus.Succeeded, second.Status);
    }
}
