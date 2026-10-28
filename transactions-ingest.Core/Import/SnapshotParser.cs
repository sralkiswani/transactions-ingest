namespace transactions_ingest.Core.Import;

/// <summary>One source's snapshot after validation.</summary>
public sealed record ParsedSnapshot(
    string SourceSystem,
    int Fetched,
    IReadOnlyDictionary<int, ParsedTransaction> Valid,
    IReadOnlySet<int> PresentKeys,
    int UnparseableKeys,
    IReadOnlySet<int> DuplicateKeys,
    IReadOnlyList<RecordRejection> Rejections)
{
    /// <summary>Records not used: validation failures, unparseable keys, and every record sharing a duplicate key.</summary>
    public int Rejected => Fetched - Valid.Count;

    /// <summary>
    /// False when some record cannot be matched to exactly one key, so absence from the snapshot proves
    /// nothing and revocation must be skipped for this run (D4).
    /// </summary>
    public bool RevocationAllowed => UnparseableKeys == 0 && DuplicateKeys.Count == 0;
}

/// <summary>Validates a whole snapshot and works out which keys are present in it (D4). Pure.</summary>
public static class SnapshotParser
{
    public static ParsedSnapshot Parse(string sourceSystem, IReadOnlyList<RawTransaction> records, RunClock clock)
    {
        var valid = new Dictionary<int, ParsedTransaction>();
        var present = new HashSet<int>();
        var duplicates = new HashSet<int>();
        var rejections = new List<RecordRejection>();
        var unparseable = 0;

        foreach (var raw in records)
        {
            switch (TransactionValidator.Validate(raw, sourceSystem, clock))
            {
                case UnparseableKey u:
                    unparseable++;
                    rejections.Add(new RecordRejection(null, [u.Reason]));
                    break;

                case RejectedRecord r:
                    // The key is in the snapshot even though the record is unusable, so its row must not be revoked.
                    if (!present.Add(r.TransactionId))
                    {
                        duplicates.Add(r.TransactionId);
                    }
                    rejections.Add(new RecordRejection(r.TransactionId, r.Reasons));
                    break;

                case ValidRecord v:
                    var id = v.Transaction.TransactionId;
                    if (present.Add(id))
                    {
                        valid[id] = v.Transaction;
                    }
                    else
                    {
                        duplicates.Add(id);
                    }
                    break;
            }
        }

        // A duplicated key is ambiguous: none of its records is trusted.
        foreach (var id in duplicates)
        {
            valid.Remove(id);
            rejections.Add(new RecordRejection(id, ["TransactionId appears more than once in the snapshot."]));
        }

        return new ParsedSnapshot(sourceSystem, records.Count, valid, present, unparseable, duplicates, rejections);
    }
}
