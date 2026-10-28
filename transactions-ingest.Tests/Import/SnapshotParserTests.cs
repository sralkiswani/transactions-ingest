using static transactions_ingest.Tests.Import.ImportTestData;

namespace transactions_ingest.Tests.Import;

public class SnapshotParserTests
{
    [Fact]
    public void Valid_records_are_keyed_by_transaction_id()
    {
        var snapshot = Snapshot(Raw(id: "1"), Raw(id: "2"));

        Assert.Equal(2, snapshot.Fetched);
        Assert.Equal([1, 2], snapshot.Valid.Keys.Order());
        Assert.Equal(0, snapshot.Rejected);
        Assert.True(snapshot.RevocationAllowed);
    }

    [Fact]
    public void Rejected_record_keeps_its_key_present_but_is_not_valid()
    {
        var snapshot = Snapshot(Raw(id: "1"), Raw(id: "2", amount: "oops"));

        Assert.Contains(2, snapshot.PresentKeys);
        Assert.False(snapshot.Valid.ContainsKey(2));
        Assert.Equal(1, snapshot.Rejected);
        Assert.True(snapshot.RevocationAllowed);
    }

    [Fact]
    public void Unparseable_key_disallows_revocation()
    {
        var snapshot = Snapshot(Raw(id: "1"), Raw(id: "x"));

        Assert.Equal(1, snapshot.UnparseableKeys);
        Assert.Equal(1, snapshot.Rejected);
        Assert.False(snapshot.RevocationAllowed);
        Assert.True(snapshot.Valid.ContainsKey(1));
    }

    [Fact]
    public void Duplicate_key_rejects_every_record_with_it_and_disallows_revocation()
    {
        var snapshot = Snapshot(Raw(id: "1"), Raw(id: "2", amount: "1.00"), Raw(id: "2", amount: "2.00"));

        Assert.Contains(2, snapshot.DuplicateKeys);
        Assert.Contains(2, snapshot.PresentKeys);
        Assert.False(snapshot.Valid.ContainsKey(2));
        Assert.Equal(2, snapshot.Rejected);
        Assert.False(snapshot.RevocationAllowed);
    }

    [Fact]
    public void Duplicate_of_a_rejected_record_also_counts_as_duplicate()
    {
        var snapshot = Snapshot(Raw(id: "3", amount: "bad"), Raw(id: "3"));

        Assert.Contains(3, snapshot.DuplicateKeys);
        Assert.False(snapshot.Valid.ContainsKey(3));
        Assert.False(snapshot.RevocationAllowed);
    }

    [Fact]
    public void Empty_snapshot_allows_revocation()
    {
        var snapshot = Snapshot();

        Assert.Equal(0, snapshot.Fetched);
        Assert.Empty(snapshot.PresentKeys);
        Assert.True(snapshot.RevocationAllowed);
    }
}
