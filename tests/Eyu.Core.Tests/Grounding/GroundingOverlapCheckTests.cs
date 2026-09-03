using Eyu.Core.Grounding;
using Eyu.Core.Records;
using Xunit;

namespace Eyu.Core.Tests.Grounding;

public class GroundingOverlapCheckTests
{
    private static RawRecord Record(string id, params (string Key, string Value)[] fields) =>
        new(id, fields.ToDictionary(f => f.Key, f => (string?)f.Value));

    [Fact]
    public void A_claim_backed_by_its_cited_source_content_is_supported()
    {
        // Same shape as the real gap this check closes: two aviation-sdr records describing the
        // same cracked floorbeam (`AALA202601050865`/`AALA202601056048`, BD-20260903-02).
        var records = new[]
        {
            Record("AALA202601050865", ("part_name", "FLOORBEAM"), ("part_condition", "CRACKED"),
                ("discrepancy", "CRACK IN PASSENGER CABIN FLOORBEAM AT BS 540, RBL 2.")),
            Record("AALA202601056048", ("part_name", "FLOORBEAM"), ("part_condition", "CRACKED"),
                ("discrepancy", "CRACK IN PASSENGER CABIN FLOORBEAM AT BS 540, LBL 2.")),
        };
        var claim = GroundedClaim.Create(
            "both records describe a cracked floorbeam in the passenger cabin",
            sources: [new SourceRef("AALA202601050865"), new SourceRef("AALA202601056048")]);

        Assert.True(GroundingOverlapCheck.IsSupportedBy(claim, records));
    }

    [Fact]
    public void A_claim_citing_a_valid_but_unrelated_source_is_not_supported()
    {
        // The exact gap BD-20260903-02 found: the live quality measurement only checks that a
        // cited source id exists among the given records, not that its content backs the claim.
        // Here the source id is real, so the existing id-validity check would pass this — this
        // check is what catches it instead.
        var records = new[]
        {
            Record("0252022001", ("plant_name", "Vogtle 3"), ("title", "Automatic Reactor Trip Signal")),
        };
        var claim = GroundedClaim.Create(
            "the aircraft floorbeam was cracked during base maintenance",
            sources: [new SourceRef("0252022001")]);

        Assert.False(GroundingOverlapCheck.IsSupportedBy(claim, records));
    }

    [Fact]
    public void A_source_id_absent_from_the_given_records_contributes_no_support()
    {
        var records = Array.Empty<RawRecord>();
        var claim = GroundedClaim.Create(
            "the invoice total is $100",
            sources: [new SourceRef("rec-not-given")]);

        Assert.False(GroundingOverlapCheck.IsSupportedBy(claim, records));
    }

    [Fact]
    public void A_field_scoped_source_ref_only_counts_that_fields_content()
    {
        var records = new[]
        {
            Record("rec-1", ("total", "100 dollars"), ("notes", "unrelated shipment weight and volume")),
        };
        // Cites only the "notes" field, so the "total" field's overlap with the claim must not count.
        var claim = GroundedClaim.Create(
            "the invoice total is 100 dollars",
            sources: [new SourceRef("rec-1", FieldName: "notes")]);

        Assert.False(GroundingOverlapCheck.IsSupportedBy(claim, records));
    }

    [Fact]
    public void Overlap_ratio_below_the_configured_minimum_is_not_supported()
    {
        var records = new[] { Record("rec-1", ("field", "alpha bravo")) };
        // Only "alpha" (1 of 4 significant tokens) overlaps -> 0.25 ratio, below the 0.3 default.
        var claim = GroundedClaim.Create(
            "alpha charlie delta echo",
            sources: [new SourceRef("rec-1")]);

        Assert.False(GroundingOverlapCheck.IsSupportedBy(claim, records));
        Assert.True(GroundingOverlapCheck.IsSupportedBy(claim, records, minOverlapRatio: 0.2));
    }
}
