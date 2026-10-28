using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace transactions_ingest.Core.Persistence;

/// <summary>
/// Stores decimal amounts as whole cents (INTEGER) so SQLite can sort and compare them exactly (D7).
/// </summary>
public sealed class DecimalToCentsConverter() : ValueConverter<decimal, long>(
    v => ToCents(v),
    v => FromCents(v))
{
    public static long ToCents(decimal value)
    {
        var cents = value * 100m;
        if (cents != decimal.Truncate(cents))
        {
            throw new InvalidOperationException(
                $"Amount {value} has more than 2 decimal places and cannot be stored exactly.");
        }

        return (long)cents;
    }

    public static decimal FromCents(long cents) => cents / 100m;
}

/// <summary>
/// Rejects non-UTC DateTime values on write and marks values read back as UTC (D7).
/// </summary>
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => EnsureUtc(v),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
{
    public static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : throw new InvalidOperationException(
                $"DateTime values must be UTC; got Kind={value.Kind}.");
}
