using System.Globalization;
using transactions_ingest.Core.Domain;
using transactions_ingest.Core.Import;

namespace transactions_ingest.Tests.Import;

/// <summary>
/// Shared fixtures for the pure import tests. Defaults line up, so <see cref="Raw"/> with an ID matches
/// <see cref="Existing"/> with the same ID and time: no field changes unless a test sets one.
/// </summary>
internal static class ImportTestData
{
    public const string Source = "Uptown";

    /// <summary>Has a fractional second so boundary values are not all whole seconds.</summary>
    public static readonly DateTime Now = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc).AddTicks(5_000_000);

    public static readonly DateTime InWindow = Now.AddHours(-1);

    public static readonly DateTime PastWindow = Now.AddHours(-30);

    public static RunClock Clock() => new(Now, TimeSpan.FromMinutes(5));

    public static string Iso(DateTime utc) =>
        utc.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);

    public static RawTransaction Raw(
        string? id = "1",
        string? card = "***1111",
        string? location = "LOC-1",
        string? product = "Coffee",
        string? amount = "10.00",
        string? time = null,
        string? sourceSystem = null) =>
        new(id, card, location, product, amount, time ?? Iso(InWindow), sourceSystem);

    public static RawTransaction Raw(int id, DateTime time, string amount = "10.00") =>
        Raw(id: id.ToString(CultureInfo.InvariantCulture), amount: amount, time: Iso(time));

    public static Transaction Existing(
        int id,
        TransactionStatus status = TransactionStatus.Active,
        DateTime? time = null,
        decimal amount = 10.00m) =>
        new()
        {
            Id = 1000 + id,
            SourceSystem = Source,
            TransactionId = id,
            CardNumber = "***1111",
            LocationCode = "LOC-1",
            ProductName = "Coffee",
            Amount = amount,
            TransactionTime = time ?? InWindow,
            Status = status,
            CreatedAt = Now.AddDays(-2),
            UpdatedAt = Now.AddDays(-2),
        };

    public static ParsedSnapshot Snapshot(params RawTransaction[] records) =>
        SnapshotParser.Parse(Source, records, Clock());

    public static ReconciliationPlan Plan(ParsedSnapshot snapshot, params Transaction[] existing) =>
        ReconciliationPlanner.Plan(snapshot, existing, Clock());
}
