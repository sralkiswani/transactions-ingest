using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace transactions_ingest.Core.Import.Mock;

/// <summary>
/// Locates the mock source files and, in Development, generates any that are missing (D10). Generated
/// timestamps are absolute and written once, so later imports see the same data until someone edits or
/// deletes a file.
/// </summary>
public sealed class MockSourceFiles(
    IOptions<ImportOptions> options,
    IHostEnvironment environment,
    TimeProvider timeProvider,
    ILogger<MockSourceFiles> logger)
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public string Directory =>
        Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.MockSources.Directory));

    public string PathFor(string sourceName) => Path.Combine(Directory, FileName(sourceName));

    /// <summary>"Store Uptown" → "store-uptown.json".</summary>
    public static string FileName(string sourceName)
    {
        var slug = new StringBuilder();
        foreach (var c in sourceName.Trim().ToLowerInvariant())
        {
            slug.Append(char.IsAsciiLetterOrDigit(c) ? c : '-');
        }
        return slug.ToString().Trim('-') + ".json";
    }

    /// <summary>Writes a sample file for each configured source whose file does not exist yet.</summary>
    public void EnsureGenerated()
    {
        System.IO.Directory.CreateDirectory(Directory);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        // Whole minutes keep the files readable.
        var anchor = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);

        foreach (var (name, index) in options.Value.MockSources.Names.Select((n, i) => (n, i)))
        {
            var path = PathFor(name);
            if (File.Exists(path))
            {
                continue;
            }
            File.WriteAllText(path, JsonSerializer.Serialize(SampleRecords(name, index, anchor), WriteOptions));
            logger.LogInformation("Generated mock source file for {Source} at {Path}.", name, path);
        }
    }

    /// <summary>
    /// Sample data in the ticket's shape (docs/ticket.md), chosen by the source's position in config. Covers a
    /// card-less payment, a refund, rows older than 24h (finalized on the first import), and TransactionId
    /// 100001 used by two sources.
    /// </summary>
    private static List<SampleRecord> SampleRecords(string sourceSystem, int sourceIndex, DateTime anchor)
    {
        // The ticket's location codes are the source name's last word in capitals: Store Uptown → UPTOWN.
        var location = sourceSystem.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last().ToUpperInvariant();

        SampleRecord R(int id, string card, string product, decimal amount, double hoursAgo) =>
            new(sourceSystem, id, card, location, product, amount,
                anchor.AddHours(-hoursAgo).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));

        return (sourceIndex % 3) switch
        {
            0 =>
            [
                R(100001, "***1111", "Wireless Mouse", 24.99m, 1),
                R(100002, "***2222", "USB-C Cable", 9.50m, 3),
                R(100003, "***3333", "Gift Card", 25.00m, 6),
                R(100004, "***4444", "Laptop Stand", 39.99m, 30),
            ],
            1 =>
            [
                R(200001, "***5555", "Notebook", 7.80m, 2),
                R(200002, "", "Newspaper", 2.50m, 4),
                R(200003, "***6666", "Desk Lamp", 18.50m, 26),
            ],
            _ =>
            [
                R(100001, "***7777", "E-Book", 9.99m, 1.5),
                R(300002, "***8888", "Headphones", 59.90m, 5),
                R(300003, "***8888", "Headphones Refund", -59.90m, 2),
            ],
        };
    }

    private sealed record SampleRecord(
        string SourceSystem,
        int TransactionId,
        string CardNumber,
        string LocationCode,
        string ProductName,
        decimal Amount,
        string TransactionTime);
}
