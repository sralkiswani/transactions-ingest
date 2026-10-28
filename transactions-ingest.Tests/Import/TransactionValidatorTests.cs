using transactions_ingest.Core.Import;
using static transactions_ingest.Tests.Import.ImportTestData;

namespace transactions_ingest.Tests.Import;

public class TransactionValidatorTests
{
    private static ValidationOutcome Validate(RawTransaction raw) => TransactionValidator.Validate(raw, Source, Clock());

    private static ParsedTransaction AssertValid(RawTransaction raw)
    {
        var outcome = Validate(raw);
        return Assert.IsType<ValidRecord>(outcome).Transaction;
    }

    private static RejectedRecord AssertRejected(RawTransaction raw) => Assert.IsType<RejectedRecord>(Validate(raw));

    [Fact]
    public void Valid_record_parses_every_field()
    {
        var parsed = AssertValid(Raw(id: "42", card: "***1234", location: "LOC-9", product: "Tea", amount: "-3.50"));

        Assert.Equal(42, parsed.TransactionId);
        Assert.Equal("***1234", parsed.CardNumber);
        Assert.Equal("LOC-9", parsed.LocationCode);
        Assert.Equal("Tea", parsed.ProductName);
        Assert.Equal(-3.50m, parsed.Amount);
        Assert.Equal(InWindow, parsed.TransactionTime);
        Assert.Equal(DateTimeKind.Utc, parsed.TransactionTime.Kind);
    }

    // --- Text fields ---

    [Theory]
    [InlineData("location")]
    [InlineData("product")]
    public void Text_field_of_19_chars_is_accepted_and_20_is_rejected(string field)
    {
        AssertValid(WithText(field, new string('x', 19)));
        AssertRejected(WithText(field, new string('x', 20)));
    }

    [Theory]
    [InlineData("location", null)]
    [InlineData("location", "")]
    [InlineData("location", "   ")]
    [InlineData("product", null)]
    [InlineData("product", "")]
    [InlineData("product", "   ")]
    public void Missing_or_blank_required_text_is_rejected(string field, string? value)
    {
        AssertRejected(WithText(field, value));
    }

    private static RawTransaction WithText(string field, string? value) =>
        field == "location" ? Raw(location: value) : Raw(product: value);

    // --- CardNumber ---

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("1111", "1111")]
    [InlineData("***1111", "***1111")]
    [InlineData("***************1111", "***************1111")]
    public void Card_number_accepted_forms(string? card, string? stored)
    {
        Assert.Equal(stored, AssertValid(Raw(card: card)).CardNumber);
    }

    [Theory]
    [InlineData("****************1111")] // 16 asterisks, 20 chars
    [InlineData("111")]
    [InlineData("11111")]
    [InlineData("**11*1")]
    [InlineData("abcd")]
    [InlineData("4111111111111111")]
    [InlineData("1111\n")]
    [InlineData(" 1111")]
    [InlineData("١٢٣٤")] // Arabic-Indic digits
    public void Card_number_rejected_forms(string card)
    {
        AssertRejected(Raw(card: card));
    }

    [Fact]
    public void Card_number_value_never_appears_in_rejection_reasons()
    {
        var rejected = AssertRejected(Raw(card: "4111111111111111"));

        Assert.DoesNotContain(rejected.Reasons, r => r.Contains("4111", StringComparison.Ordinal));
    }

    // --- Amount ---

    [Theory]
    [InlineData("-12.34", "-12.34")]
    [InlineData("0", "0")]
    [InlineData("10.0", "10.00")]
    [InlineData("9999999999999999.99", "9999999999999999.99")]
    [InlineData("-9999999999999999.99", "-9999999999999999.99")]
    public void Amount_accepted_forms(string amount, string expected)
    {
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), AssertValid(Raw(amount: amount)).Amount);
    }

    [Theory]
    [InlineData("12.345")]
    [InlineData("1e3")]
    [InlineData("12,34")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData(" 1.00")]
    [InlineData("10000000000000000.00")]
    public void Amount_rejected_forms(string? amount)
    {
        AssertRejected(Raw(amount: amount));
    }

    // --- TransactionTime ---

    [Fact]
    public void Time_with_Z_is_accepted_as_utc()
    {
        var parsed = AssertValid(Raw(time: "2026-10-28T11:00:00Z"));

        Assert.Equal(new DateTime(2026, 10, 28, 11, 0, 0, DateTimeKind.Utc), parsed.TransactionTime);
        Assert.Equal(DateTimeKind.Utc, parsed.TransactionTime.Kind);
    }

    [Fact]
    public void Time_with_offset_is_converted_to_utc()
    {
        var parsed = AssertValid(Raw(time: "2026-10-28T13:00:00.25+02:00"));

        Assert.Equal(new DateTime(2026, 10, 28, 11, 0, 0, 250, DateTimeKind.Utc), parsed.TransactionTime);
        Assert.Equal(DateTimeKind.Utc, parsed.TransactionTime.Kind);
    }

    [Theory]
    [InlineData("2026-10-28T11:00:00")]
    [InlineData("10/28/2026 11:00:00 +00:00")]
    [InlineData("not a time")]
    [InlineData("")]
    [InlineData(null)]
    public void Time_without_zone_or_unparseable_is_rejected(string? time)
    {
        // `with` rather than Raw(time: ...), which substitutes a default for null.
        AssertRejected(Raw() with { TransactionTime = time });
    }

    [Fact]
    public void Time_up_to_the_future_tolerance_is_accepted_and_one_tick_later_is_rejected()
    {
        AssertValid(Raw(time: Iso(Now.AddMinutes(5))));
        AssertRejected(Raw(time: Iso(Now.AddMinutes(5).AddTicks(1))));
    }

    // --- SourceSystem ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(Source)]
    public void Missing_or_matching_source_system_is_accepted(string? sourceSystem)
    {
        AssertValid(Raw(sourceSystem: sourceSystem));
    }

    [Fact]
    public void Mismatched_source_system_is_rejected_with_its_key()
    {
        var rejected = AssertRejected(Raw(id: "7", sourceSystem: "Online"));

        Assert.Equal(7, rejected.TransactionId);
    }

    // --- TransactionId ---

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("2147483648")]
    [InlineData(" 1")]
    [InlineData("1.0")]
    public void Unparseable_transaction_id_gives_unparseable_key(string? id)
    {
        Assert.IsType<UnparseableKey>(Validate(Raw(id: id)));
    }

    [Fact]
    public void All_reasons_are_reported_together()
    {
        var rejected = AssertRejected(Raw(location: "", amount: "1.005", card: "12"));

        Assert.Equal(3, rejected.Reasons.Count);
    }
}
