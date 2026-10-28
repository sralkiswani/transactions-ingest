using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using transactions_ingest.Core.Domain;
using transactions_ingest.Core.Import;
using transactions_ingest.Server.Contracts;

namespace transactions_ingest.Tests.Api;

public class ApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Import_then_list()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var summary = await ImportAsync(client);

        Assert.Equal(ImportRunStatus.Succeeded, summary.Status);
        Assert.Equal(["Store Uptown", "Store Downtown", "Online"], summary.Sources.Select(s => s.SourceSystem));
        Assert.True(File.Exists(factory.DatabasePath), "The test must use its own database file.");

        var page = await ListAsync(client, "?pageSize=100");
        Assert.Equal(summary.Sources.Sum(s => s.Inserted), page.TotalCount);
        Assert.Equal(page.Items.OrderByDescending(t => t.TransactionTime).Select(t => t.Id), page.Items.Select(t => t.Id));
        // Same TransactionId in two sources is two rows.
        Assert.Equal(["Online", "Store Uptown"], page.Items.Where(t => t.TransactionId == 100001).Select(t => t.SourceSystem).Order());
        // Rows older than 24h are finalized on the first import.
        Assert.Equal(summary.Finalized, page.Items.Count(t => t.Status == TransactionStatus.Finalized));
        Assert.True(summary.Finalized > 0);
    }

    [Fact]
    public async Task Amounts_are_returned_with_two_decimals()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        await ImportAsync(client);

        var json = await client.GetStringAsync("/api/transactions?pageSize=100", Ct);

        Assert.Contains("\"amount\":24.99,", json);
        Assert.Contains("\"amount\":18.50,", json);
        Assert.Contains("\"amount\":-59.90,", json);
        Assert.Contains("\"amount\":25.00,", json);
    }

    [Fact]
    public async Task Importing_twice_changes_neither_transactions_nor_audits()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        await ImportAsync(client);
        var before = await client.GetStringAsync("/api/transactions?pageSize=100", Ct);
        var auditsBefore = await CountAuditsAsync(factory);

        factory.Time.Advance(TimeSpan.FromHours(1));
        var second = await ImportAsync(client);

        Assert.Equal(before, await client.GetStringAsync("/api/transactions?pageSize=100", Ct));
        Assert.Equal(auditsBefore, await CountAuditsAsync(factory));
        Assert.All(second.Sources, s => Assert.Equal((0, 0, 0, 0, 0), (s.Inserted, s.Updated, s.Revoked, s.Reinstated, s.Ignored)));
    }

    [Fact]
    public async Task Import_while_one_is_running_returns_409()
    {
        var blocker = new BlockingSource("Blocker");
        using var factory = new ApiFactory { ConfigureServices = s => s.AddSingleton<ITransactionSource>(blocker) };
        var client = factory.CreateClient();

        var first = client.PostAsync("/api/import", null, Ct);
        await blocker.Entered.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        using var second = await client.PostAsync("/api/import", null, Ct);
        blocker.Release();
        using var firstResponse = await first;

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
    }

    [Fact]
    public async Task Paging_and_filters()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        await ImportAsync(client);

        var all = await ListAsync(client, "?pageSize=100");
        var first = await ListAsync(client, "?page=1&pageSize=3");
        var second = await ListAsync(client, "?page=2&pageSize=3");
        Assert.Equal(all.TotalCount, first.TotalCount);
        Assert.Equal(all.Items.Take(6).Select(t => t.Id), first.Items.Concat(second.Items).Select(t => t.Id));

        var online = await ListAsync(client, "?source=Online");
        Assert.NotEmpty(online.Items);
        Assert.All(online.Items, t => Assert.Equal("Online", t.SourceSystem));

        var finalized = await ListAsync(client, "?status=Finalized");
        Assert.NotEmpty(finalized.Items);
        Assert.All(finalized.Items, t => Assert.Equal(TransactionStatus.Finalized, t.Status));

        var beyond = await ListAsync(client, "?page=50&pageSize=100");
        Assert.Empty(beyond.Items);
        Assert.Equal(all.TotalCount, beyond.TotalCount);
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?page=abc")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?status=Bogus")]
    [InlineData("?status=7")]
    [InlineData("?source=ABCDEFGHIJKLMNOPQRST")]
    public async Task Invalid_list_query_returns_400_problem_details(string query)
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/transactions" + query, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Audit_of_a_transaction_lists_its_changes()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        await ImportAsync(client);
        var finalized = (await ListAsync(client, "?status=Finalized")).Items[0];

        var audit = await client.GetFromJsonAsync<List<AuditEntryDto>>($"/api/transactions/{finalized.Id}/audit", ApiFactory.Json, Ct);

        Assert.Equal([AuditChangeType.Created, AuditChangeType.Finalized], audit!.Select(a => a.ChangeType));
    }

    [Fact]
    public async Task Audit_of_unknown_id_returns_404_problem_details()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/transactions/999999/audit", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Unhandled_error_outside_development_is_problem_details_without_exception_details()
    {
        using var factory = new ApiFactory
        {
            EnvironmentName = "Production",
            ConfigureServices = s => s.AddSingleton<IImportLock>(new ThrowingLock()),
        };
        var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/import", null, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(ThrowingLock.Secret, body);
        Assert.DoesNotContain(" at ", body);
    }

    private static async Task<ImportRunSummary> ImportAsync(HttpClient client)
    {
        using var response = await client.PostAsync("/api/import", null, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ImportRunSummary>(ApiFactory.Json, Ct))!;
    }

    private static async Task<PagedResult<TransactionDto>> ListAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<PagedResult<TransactionDto>>("/api/transactions" + query, ApiFactory.Json, Ct))!;

    private static async Task<int> CountAuditsAsync(ApiFactory factory)
    {
        await using var db = factory.CreateDbContext();
        return await db.TransactionAudits.CountAsync(Ct);
    }

    private sealed class BlockingSource(string name) : ITransactionSource
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string SourceSystem => name;

        public Task Entered => _entered.Task;

        public void Release() => _release.TrySetResult();

        public async Task<IReadOnlyList<RawTransaction>> FetchAsync(CancellationToken cancellationToken)
        {
            _entered.TrySetResult();
            await _release.Task;
            return [];
        }
    }

    private sealed class ThrowingLock : IImportLock
    {
        public const string Secret = "internal detail that must not leak";

        public IDisposable? TryAcquire() => throw new InvalidOperationException(Secret);
    }
}
