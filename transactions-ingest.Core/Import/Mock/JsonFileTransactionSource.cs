using System.Text.Json;

namespace transactions_ingest.Core.Import.Mock;

/// <summary>
/// A mock source backed by a JSON file: an array of objects with the ticket's field names (camelCase or any
/// case). Every value reaches <see cref="RawTransaction"/> as text, so validation stays in one place (D10).
/// </summary>
public sealed class JsonFileTransactionSource(string sourceSystem, string path) : ITransactionSource
{
    public string SourceSystem => sourceSystem;

    public string FilePath => path;

    /// <summary>Throws if the file is missing or not a JSON array, so the source fails rather than looking empty (D4).</summary>
    public async Task<IReadOnlyList<RawTransaction>> FetchAsync(CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"Expected a JSON array in '{path}'.");
        }

        var records = new List<RawTransaction>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            if (item.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in item.EnumerateObject())
                {
                    fields[property.Name] = AsText(property.Value);
                }
            }
            // A non-object item yields a record with no key, which the parser counts as unparseable.

            records.Add(new RawTransaction(
                Field(fields, "transactionId"),
                Field(fields, "cardNumber"),
                Field(fields, "locationCode"),
                Field(fields, "productName"),
                Field(fields, "amount"),
                Field(fields, "transactionTime"),
                Field(fields, "sourceSystem")));
        }
        return records;
    }

    /// <summary>Numbers come through as their raw JSON text, never via double (D10).</summary>
    internal static string? AsText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Null => null,
        _ => value.GetRawText(),
    };

    private static string? Field(Dictionary<string, string?> fields, string name) =>
        fields.TryGetValue(name, out var value) ? value : null;
}
