using System.Globalization;
using transactions_ingest.Core.Domain;

namespace transactions_ingest.Core.Import;

/// <summary>One field that differs between the stored row and the incoming record, formatted for the audit.</summary>
public sealed record FieldChange(string FieldName, string? OldValue, string? NewValue);

/// <summary>An existing row and the incoming record it should take on.</summary>
public sealed record RowChange(Transaction Row, ParsedTransaction Incoming, IReadOnlyList<FieldChange> Changes);

/// <summary>What one source's snapshot means for the stored rows. Nothing is applied yet.</summary>
public sealed record ReconciliationPlan(
    IReadOnlyList<ParsedTransaction> Inserts,
    IReadOnlyList<RowChange> Updates,
    IReadOnlyList<RowChange> Reinstatements,
    IReadOnlyList<Transaction> Revocations,
    int Ignored,
    int RevocationCandidates,
    bool RevocationSkipped);

/// <summary>
/// Decides inserts, updates, reinstatements and revocations for one source (D3, D4, D5). Pure: takes the
/// existing rows as plain objects and returns a plan, so the rules are testable without a database.
/// </summary>
public static class ReconciliationPlanner
{
    /// <param name="existing">
    /// This source's rows that are in the window or whose key is in the snapshot. Rows outside both sets
    /// cannot be affected by this run.
    /// </param>
    public static ReconciliationPlan Plan(ParsedSnapshot snapshot, IReadOnlyCollection<Transaction> existing, RunClock clock)
    {
        var byId = new Dictionary<int, Transaction>();
        foreach (var row in existing)
        {
            if (!string.Equals(row.SourceSystem, snapshot.SourceSystem, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Row {row.Id} belongs to '{row.SourceSystem}', not '{snapshot.SourceSystem}'.", nameof(existing));
            }
            byId.Add(row.TransactionId, row);
        }

        var inserts = new List<ParsedTransaction>();
        var updates = new List<RowChange>();
        var reinstatements = new List<RowChange>();
        var ignored = 0;

        foreach (var (id, incoming) in snapshot.Valid.OrderBy(kv => kv.Key))
        {
            if (!byId.TryGetValue(id, out var row))
            {
                inserts.Add(incoming);
                continue;
            }

            var changes = Diff(row, incoming);
            if (IsLocked(row, clock))
            {
                // Only a real difference counts, so re-running an unchanged snapshot reports zero (D5).
                if (changes.Count > 0)
                {
                    ignored++;
                }
                continue;
            }

            if (row.Status == TransactionStatus.Revoked)
            {
                reinstatements.Add(new RowChange(row, incoming, changes));
            }
            else if (changes.Count > 0)
            {
                updates.Add(new RowChange(row, incoming, changes));
            }
        }

        var candidates = existing
            .Where(r => r.Status == TransactionStatus.Active && clock.IsMutable(r.TransactionTime))
            .ToList();
        var missing = candidates
            .Where(r => !snapshot.PresentKeys.Contains(r.TransactionId))
            .OrderBy(r => r.TransactionId)
            .ToList();

        return new ReconciliationPlan(
            inserts,
            updates,
            reinstatements,
            snapshot.RevocationAllowed ? missing : [],
            ignored,
            candidates.Count,
            RevocationSkipped: !snapshot.RevocationAllowed);
    }

    /// <summary>Locked rows never change again; the stored TransactionTime decides, not the incoming one.</summary>
    private static bool IsLocked(Transaction row, RunClock clock) =>
        row.Status == TransactionStatus.Finalized || !clock.IsMutable(row.TransactionTime);

    private static List<FieldChange> Diff(Transaction row, ParsedTransaction incoming)
    {
        var changes = new List<FieldChange>();

        AddIfDifferent(changes, nameof(Transaction.CardNumber), row.CardNumber, incoming.CardNumber);
        AddIfDifferent(changes, nameof(Transaction.LocationCode), row.LocationCode, incoming.LocationCode);
        AddIfDifferent(changes, nameof(Transaction.ProductName), row.ProductName, incoming.ProductName);

        // decimal equality is by value (10.0 == 10.00); DateTime equality is by ticks.
        if (row.Amount != incoming.Amount)
        {
            changes.Add(new FieldChange(nameof(Transaction.Amount), FormatAmount(row.Amount), FormatAmount(incoming.Amount)));
        }
        if (row.TransactionTime != incoming.TransactionTime)
        {
            changes.Add(new FieldChange(
                nameof(Transaction.TransactionTime), FormatTime(row.TransactionTime), FormatTime(incoming.TransactionTime)));
        }

        return changes;
    }

    private static void AddIfDifferent(List<FieldChange> changes, string field, string? stored, string? incoming)
    {
        if (!string.Equals(stored, incoming, StringComparison.Ordinal))
        {
            changes.Add(new FieldChange(field, stored, incoming));
        }
    }

    private static string FormatAmount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatTime(DateTime value) => value.ToString("O", CultureInfo.InvariantCulture);
}
