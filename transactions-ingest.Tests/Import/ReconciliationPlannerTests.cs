using transactions_ingest.Core.Domain;
using transactions_ingest.Core.Import;
using static transactions_ingest.Tests.Import.ImportTestData;

namespace transactions_ingest.Tests.Import;

public class ReconciliationPlannerTests
{
    // --- D3: inserts and updates ---

    [Fact]
    public void New_key_is_inserted()
    {
        var plan = Plan(Snapshot(Raw(id: "1")));

        Assert.Equal(1, Assert.Single(plan.Inserts).TransactionId);
        AssertNoRowChanges(plan);
    }

    [Fact]
    public void New_key_older_than_24h_is_still_inserted()
    {
        var plan = Plan(Snapshot(Raw(1, PastWindow)));

        Assert.Equal(PastWindow, Assert.Single(plan.Inserts).TransactionTime);
    }

    [Fact]
    public void Changed_fields_on_a_mutable_row_give_one_change_per_field()
    {
        var newTime = InWindow.AddMinutes(-10);
        var plan = Plan(
            Snapshot(Raw(id: "1", card: "***2222", location: "LOC-2", product: "Tea", amount: "12.5", time: Iso(newTime))),
            Existing(1));

        var update = Assert.Single(plan.Updates);
        Assert.Equal(
            [
                new FieldChange("CardNumber", "***1111", "***2222"),
                new FieldChange("LocationCode", "LOC-1", "LOC-2"),
                new FieldChange("ProductName", "Coffee", "Tea"),
                new FieldChange("Amount", "10.00", "12.50"),
                new FieldChange("TransactionTime", "2026-10-28T11:00:00.5000000Z", "2026-10-28T10:50:00.5000000Z"),
            ],
            update.Changes);
        Assert.Equal(0, plan.Ignored);
    }

    [Fact]
    public void Unchanged_mutable_row_needs_no_update()
    {
        var plan = Plan(Snapshot(Raw(id: "1")), Existing(1));

        AssertNoRowChanges(plan);
        Assert.Empty(plan.Inserts);
        Assert.Equal(0, plan.Ignored);
    }

    [Fact]
    public void Card_number_removed_by_source_is_a_change_to_null()
    {
        var update = Assert.Single(Plan(Snapshot(Raw(id: "1", card: "")), Existing(1)).Updates);

        Assert.Equal(new FieldChange("CardNumber", "***1111", null), Assert.Single(update.Changes));
    }

    // --- D3: locked rows ---

    [Fact]
    public void Change_to_finalized_row_is_ignored_and_counted()
    {
        var plan = Plan(Snapshot(Raw(1, PastWindow, amount: "99.00")), Existing(1, TransactionStatus.Finalized, PastWindow));

        AssertNoRowChanges(plan);
        Assert.Equal(1, plan.Ignored);
    }

    [Fact]
    public void Unchanged_finalized_row_is_not_counted_as_ignored()
    {
        var plan = Plan(Snapshot(Raw(1, PastWindow)), Existing(1, TransactionStatus.Finalized, PastWindow));

        AssertNoRowChanges(plan);
        Assert.Equal(0, plan.Ignored);
    }

    [Fact]
    public void Change_to_active_row_past_the_window_is_ignored()
    {
        var plan = Plan(Snapshot(Raw(1, PastWindow, amount: "99.00")), Existing(1, TransactionStatus.Active, PastWindow));

        AssertNoRowChanges(plan);
        Assert.Equal(1, plan.Ignored);
    }

    [Fact]
    public void Time_correction_on_a_mutable_row_applies_even_if_it_leaves_the_window()
    {
        var plan = Plan(Snapshot(Raw(1, PastWindow)), Existing(1));

        var change = Assert.Single(Assert.Single(plan.Updates).Changes);
        Assert.Equal("TransactionTime", change.FieldName);
    }

    [Fact]
    public void Time_correction_that_would_move_a_locked_row_into_the_window_is_ignored()
    {
        var plan = Plan(Snapshot(Raw(1, InWindow)), Existing(1, TransactionStatus.Active, PastWindow));

        AssertNoRowChanges(plan);
        Assert.Equal(1, plan.Ignored);
    }

    // --- D3: revocation ---

    [Fact]
    public void Missing_active_in_window_row_is_revoked()
    {
        var plan = Plan(Snapshot(Raw(id: "1")), Existing(1), Existing(2));

        Assert.Equal(2, Assert.Single(plan.Revocations).TransactionId);
        Assert.Equal(2, plan.RevocationCandidates);
    }

    [Fact]
    public void Missing_active_row_past_the_window_is_not_revoked()
    {
        var plan = Plan(Snapshot(), Existing(1, TransactionStatus.Active, PastWindow));

        Assert.Empty(plan.Revocations);
        Assert.Equal(0, plan.RevocationCandidates);
    }

    [Fact]
    public void Missing_revoked_row_is_left_alone()
    {
        var plan = Plan(Snapshot(), Existing(1, TransactionStatus.Revoked));

        AssertNoRowChanges(plan);
        Assert.Equal(0, plan.RevocationCandidates);
    }

    [Fact]
    public void Missing_future_dated_row_within_tolerance_is_revoked()
    {
        var plan = Plan(Snapshot(), Existing(1, TransactionStatus.Active, Now.AddMinutes(3)));

        Assert.Single(plan.Revocations);
    }

    // --- D3: reinstatement ---

    [Fact]
    public void Revoked_in_window_row_that_reappears_is_reinstated()
    {
        var plan = Plan(Snapshot(Raw(id: "1")), Existing(1, TransactionStatus.Revoked));

        var reinstated = Assert.Single(plan.Reinstatements);
        Assert.Empty(reinstated.Changes);
        Assert.Empty(plan.Updates);
    }

    [Fact]
    public void Reinstated_row_carries_its_field_changes_and_is_not_also_an_update()
    {
        var plan = Plan(Snapshot(Raw(id: "1", amount: "11.00")), Existing(1, TransactionStatus.Revoked));

        var reinstated = Assert.Single(plan.Reinstatements);
        Assert.Equal(new FieldChange("Amount", "10.00", "11.00"), Assert.Single(reinstated.Changes));
        Assert.Empty(plan.Updates);
    }

    [Fact]
    public void Revoked_row_past_the_window_that_reappears_stays_revoked()
    {
        var unchanged = Plan(Snapshot(Raw(1, PastWindow)), Existing(1, TransactionStatus.Revoked, PastWindow));
        var changed = Plan(Snapshot(Raw(1, PastWindow, amount: "99.00")), Existing(1, TransactionStatus.Revoked, PastWindow));

        AssertNoRowChanges(unchanged);
        Assert.Equal(0, unchanged.Ignored);
        AssertNoRowChanges(changed);
        Assert.Equal(1, changed.Ignored);
    }

    [Fact]
    public void Revoked_row_whose_reappearing_record_is_invalid_stays_revoked()
    {
        var plan = Plan(Snapshot(Raw(id: "1", amount: "bad")), Existing(1, TransactionStatus.Revoked));

        AssertNoRowChanges(plan);
        Assert.Empty(plan.Inserts);
    }

    // --- D4: keys present but unusable ---

    [Fact]
    public void Rejected_record_leaves_its_row_unchanged_and_not_revoked()
    {
        var plan = Plan(Snapshot(Raw(id: "1", amount: "bad")), Existing(1));

        AssertNoRowChanges(plan);
        Assert.Equal(1, plan.RevocationCandidates);
        Assert.False(plan.RevocationSkipped);
    }

    [Fact]
    public void Unparseable_key_applies_valid_updates_but_skips_revocation()
    {
        var plan = Plan(Snapshot(Raw(id: "1", amount: "11.00"), Raw(id: "x")), Existing(1), Existing(2));

        Assert.Single(plan.Updates);
        Assert.Empty(plan.Revocations);
        Assert.True(plan.RevocationSkipped);
    }

    [Fact]
    public void Duplicate_key_leaves_its_row_unchanged_and_skips_revocation()
    {
        var plan = Plan(Snapshot(Raw(id: "1", amount: "11.00"), Raw(id: "1", amount: "12.00")), Existing(1), Existing(2));

        AssertNoRowChanges(plan);
        Assert.True(plan.RevocationSkipped);
    }

    // --- D5: formatting-only differences are not changes ---

    [Theory]
    [InlineData("10.0", null)]
    [InlineData("10", null)]
    [InlineData("10.00", "+00:00")]
    public void Formatting_only_differences_are_not_changes(string amount, string? offset)
    {
        var time = offset is null ? Iso(InWindow) : Iso(InWindow).TrimEnd('Z') + offset;

        var plan = Plan(Snapshot(Raw(id: "1", amount: amount, time: time)), Existing(1));

        AssertNoRowChanges(plan);
    }

    [Fact]
    public void Empty_and_missing_card_number_match_a_stored_null()
    {
        var row = Existing(1);
        row.CardNumber = null;

        Assert.Empty(Plan(Snapshot(Raw(id: "1", card: "")), row).Updates);
        Assert.Empty(Plan(Snapshot(Raw(id: "1", card: null)), row).Updates);
    }

    // --- Guarding the input ---

    [Fact]
    public void Row_from_another_source_is_refused()
    {
        var row = Existing(1);
        row.SourceSystem = "Online";

        Assert.Throws<ArgumentException>(() => Plan(Snapshot(), row));
    }

    private static void AssertNoRowChanges(ReconciliationPlan plan)
    {
        Assert.Empty(plan.Updates);
        Assert.Empty(plan.Reinstatements);
        Assert.Empty(plan.Revocations);
    }
}
