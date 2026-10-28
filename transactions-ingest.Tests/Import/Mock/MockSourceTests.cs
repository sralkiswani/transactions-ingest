using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using transactions_ingest.Core.Import;
using transactions_ingest.Core.Import.Mock;
using transactions_ingest.Tests.Import.Service;
using static transactions_ingest.Tests.Import.ImportTestData;

namespace transactions_ingest.Tests.Import.Mock;

public sealed class MockSourceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "transactions-ingest-tests", Guid.NewGuid().ToString("N"));

    public MockSourceTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // --- JsonFileTransactionSource ---

    [Fact]
    public async Task Numbers_arrive_as_their_raw_json_text()
    {
        var source = Source("""
            [ { "transactionId": 1001, "amount": 10.10, "cardNumber": "***1111", "locationCode": "L", "productName": "P",
                "transactionTime": "2026-10-28T11:00:00Z" },
              { "transactionId": 12345678901234567890, "amount": 1e3 } ]
            """);

        var records = await source.FetchAsync(Ct);

        Assert.Equal("1001", records[0].TransactionId);
        Assert.Equal("10.10", records[0].Amount);
        Assert.Equal("12345678901234567890", records[1].TransactionId);
        Assert.Equal("1e3", records[1].Amount);
    }

    [Fact]
    public async Task Property_names_are_matched_case_insensitively_and_missing_or_null_fields_are_null()
    {
        var records = await Source("""[ { "TransactionId": "7", "CARDNUMBER": null } ]""").FetchAsync(Ct);

        var record = Assert.Single(records);
        Assert.Equal("7", record.TransactionId);
        Assert.Null(record.CardNumber);
        Assert.Null(record.Amount);
    }

    [Fact]
    public async Task Non_object_item_becomes_a_record_without_a_key()
    {
        var records = await Source("""[ 42 ]""").FetchAsync(Ct);

        Assert.Null(Assert.Single(records).TransactionId);
    }

    [Fact]
    public async Task Missing_file_throws()
    {
        var source = new JsonFileTransactionSource("S", Path.Combine(_dir, "missing.json"));

        await Assert.ThrowsAsync<FileNotFoundException>(() => source.FetchAsync(Ct));
    }

    [Fact]
    public async Task Non_array_root_throws()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => Source("""{ "items": [] }""").FetchAsync(Ct));
    }

    // --- MockSourceFiles ---

    [Fact]
    public void File_name_is_a_slug_of_the_source_name()
    {
        Assert.Equal("store-uptown.json", MockSourceFiles.FileName("Store Uptown"));
        Assert.Equal("online.json", MockSourceFiles.FileName("Online"));
    }

    [Fact]
    public async Task Generated_files_are_valid_and_cover_the_interesting_cases()
    {
        var files = Files(out _);

        files.EnsureGenerated();

        var parsed = new Dictionary<string, ParsedSnapshot>();
        foreach (var name in Names)
        {
            var records = await new JsonFileTransactionSource(name, files.PathFor(name)).FetchAsync(Ct);
            parsed[name] = SnapshotParser.Parse(name, records, Clock());
            Assert.Equal(0, parsed[name].Rejected);
        }
        var all = parsed.Values.SelectMany(s => s.Valid.Values).ToList();
        Assert.Contains(all, t => t.CardNumber is null);
        Assert.Contains(all, t => t.Amount < 0);
        Assert.Contains(all, t => !Clock().IsMutable(t.TransactionTime));
        Assert.True(parsed["Store Uptown"].Valid.ContainsKey(1001) && parsed["Online"].Valid.ContainsKey(1001));
    }

    [Fact]
    public void Existing_files_are_never_regenerated()
    {
        var files = Files(out var time);
        files.EnsureGenerated();
        var before = Names.Select(n => File.ReadAllText(files.PathFor(n))).ToList();

        time.Advance(TimeSpan.FromHours(5));
        files.EnsureGenerated();

        Assert.Equal(before, Names.Select(n => File.ReadAllText(files.PathFor(n))));
    }

    // --- Startup checks ---

    [Fact]
    public void Duplicate_source_names_fail_the_startup_check()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            SourceRegistrationCheck.Validate([new FakeSource("Online"), new FakeSource("Store Uptown"), new FakeSource("Online")]));

        Assert.Contains("'Online'", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABCDEFGHIJKLMNOPQRST")]
    public void Blank_or_too_long_source_name_fails_the_startup_check(string name)
    {
        Assert.Throws<InvalidOperationException>(() => SourceRegistrationCheck.Validate([new FakeSource(name)]));
    }

    [Fact]
    public void Distinct_source_names_pass_the_startup_check()
    {
        SourceRegistrationCheck.Validate([new FakeSource("Online"), new FakeSource("online")]);
    }

    [Fact]
    public void Options_validator_rejects_names_that_share_a_file_and_out_of_range_values()
    {
        var options = new ImportOptions
        {
            FutureTolerance = TimeSpan.FromHours(2),
            RevocationGuard = { MaxRevokedPercent = 150 },
            MockSources = { Names = ["Online", "online"] },
        };

        var result = new ImportOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Equal(3, result.Failures!.Count());
    }

    [Fact]
    public void Options_validator_accepts_the_defaults()
    {
        Assert.True(new ImportOptionsValidator().Validate(null, new ImportOptions { MockSources = { Names = Names } }).Succeeded);
    }

    private static readonly List<string> Names = ["Store Uptown", "Store Downtown", "Online"];

    private JsonFileTransactionSource Source(string json)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        return new JsonFileTransactionSource("S", path);
    }

    private MockSourceFiles Files(out TestTimeProvider time)
    {
        time = new TestTimeProvider(Now);
        var options = Options.Create(new ImportOptions { MockSources = { Directory = _dir, Names = Names } });
        return new MockSourceFiles(options, new TestHostEnvironment(_dir), time, NullLogger<MockSourceFiles>.Instance);
    }

    private sealed class TestHostEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
