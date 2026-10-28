using System.Globalization;
using System.Text.RegularExpressions;
using transactions_ingest.Core.Persistence;

namespace transactions_ingest.Core.Import;

/// <summary>
/// Turns a <see cref="RawTransaction"/> into a parsed record or a rejection (D2). Pure; no I/O.
/// Rejection reasons never include field values, so card data cannot leak into logs.
/// </summary>
public static class TransactionValidator
{
    /// <summary>Largest magnitude that fits decimal(18,2).</summary>
    public const decimal MaxAmount = 9_999_999_999_999_999.99m;

    // [0-9] rather than \d, which also matches non-ASCII digits; \z rather than $, which allows a trailing newline.
    private static readonly Regex CardNumberPattern =
        new(@"^\*{0,15}[0-9]{4}\z", RegexOptions.CultureInvariant);

    private const string UtcTimeFormat = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'";
    private const string OffsetTimeFormat = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz";

    public static ValidationOutcome Validate(RawTransaction raw, string sourceSystem, RunClock clock)
    {
        if (!int.TryParse(raw.TransactionId, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var id))
        {
            return new UnparseableKey("TransactionId is missing or not a 32-bit integer.");
        }

        var reasons = new List<string>();

        if (!string.IsNullOrEmpty(raw.SourceSystem) && !string.Equals(raw.SourceSystem, sourceSystem, StringComparison.Ordinal))
        {
            reasons.Add($"SourceSystem does not match the source '{sourceSystem}'.");
        }

        var locationCode = RequiredText(raw.LocationCode, nameof(RawTransaction.LocationCode), reasons);
        var productName = RequiredText(raw.ProductName, nameof(RawTransaction.ProductName), reasons);
        var cardNumber = CardNumber(raw.CardNumber, reasons);
        var amount = Amount(raw.Amount, reasons);
        var transactionTime = TransactionTime(raw.TransactionTime, clock, reasons);

        return reasons.Count == 0
            ? new ValidRecord(new ParsedTransaction(id, cardNumber, locationCode!, productName!, amount, transactionTime))
            : new RejectedRecord(id, reasons);
    }

    private static string? RequiredText(string? value, string field, List<string> reasons)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            reasons.Add($"{field} is required.");
            return null;
        }
        if (value.Length > TransactionsDbContext.MaxFieldLength)
        {
            reasons.Add($"{field} is longer than {TransactionsDbContext.MaxFieldLength} characters.");
            return null;
        }
        return value;
    }

    private static string? CardNumber(string? value, List<string> reasons)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }
        if (!CardNumberPattern.IsMatch(value))
        {
            reasons.Add("CardNumber must be empty, or up to 15 '*' followed by exactly 4 digits.");
            return null;
        }
        return value;
    }

    private static decimal Amount(string? value, List<string> reasons)
    {
        const NumberStyles plainDecimal = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
        if (!decimal.TryParse(value, plainDecimal, CultureInfo.InvariantCulture, out var amount))
        {
            reasons.Add("Amount must be a plain decimal number.");
            return 0;
        }
        if (decimal.Round(amount, 2) != amount)
        {
            reasons.Add("Amount has more than 2 decimal places.");
        }
        if (Math.Abs(amount) > MaxAmount)
        {
            reasons.Add("Amount does not fit decimal(18,2).");
        }
        return amount;
    }

    private static DateTime TransactionTime(string? value, RunClock clock, List<string> reasons)
    {
        if (!TryParseUtc(value, out var utc))
        {
            reasons.Add("TransactionTime must be ISO 8601 with 'Z' or an explicit UTC offset.");
            return default;
        }
        if (utc > clock.FutureLimit)
        {
            reasons.Add("TransactionTime is too far in the future.");
        }
        return utc;
    }

    /// <summary>Accepts only ISO 8601 with an explicit zone, so no value is silently read as local time.</summary>
    private static bool TryParseUtc(string? value, out DateTime utc)
    {
        if (DateTimeOffset.TryParseExact(value, UtcTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            || DateTimeOffset.TryParseExact(value, OffsetTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
        {
            utc = parsed.UtcDateTime;
            return true;
        }
        utc = default;
        return false;
    }
}
