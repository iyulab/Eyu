using System.Text;
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
        // same cracked floorbeam (`AALA202601050865`/`AALA202601056048`).
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
        // The exact gap this check closes: the live quality measurement only checks that a
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
    public void A_korean_claim_is_supported_by_the_record_it_restates_despite_different_particles()
    {
        // A Korean noun carries its particle: the record writes "한빛테크는", the claim "한빛테크의".
        // Whole-word matching finds almost nothing in common; character pairs find the noun.
        var records = new[]
        {
            Record("chunk-1", ("content", "한빛테크는 산업용 센서 데이터 분석 기업이다. 대표이사는 이서준이다.")),
        };
        var claim = GroundedClaim.Create("이서준은 한빛테크의 대표이사이다.", sources: [new SourceRef("chunk-1")]);

        var result = GroundingOverlapCheck.Evaluate(claim, records);

        Assert.True(result.IsSupported, $"ratio {result.Ratio}");
        Assert.True(result.ClaimTokenCount > 0);
    }

    [Fact]
    public void A_korean_claim_citing_an_unrelated_korean_record_is_not_supported()
    {
        var records = new[]
        {
            Record("chunk-1", ("content", "서진화학은 울산 공장에 공정 모니터링 소프트웨어를 도입했다.")),
        };
        var claim = GroundedClaim.Create("박도윤은 가람솔루션의 대표이사이다.", sources: [new SourceRef("chunk-1")]);

        Assert.False(GroundingOverlapCheck.IsSupportedBy(claim, records));
    }

    [Theory]
    [InlineData("東京本社は品川にある。", "本社は東京の品川にあります。")]
    [InlineData("Η εταιρεία έχει έδρα στην Αθήνα.", "Η έδρα της εταιρείας βρίσκεται στην Αθήνα.")]
    [InlineData("Das Unternehmen führt Qualitätsprüfungen durch.", "Qualitätsprüfungen führt das Unternehmen durch.")]
    public void Text_outside_ascii_is_tokenized_rather_than_ignored(string record, string claimText)
    {
        // Before, only [A-Za-z0-9] counted: a claim written entirely outside ASCII had no tokens and
        // was reported unsupported whatever its source said.
        var records = new[] { Record("rec-1", ("content", record)) };
        var claim = GroundedClaim.Create(claimText, sources: [new SourceRef("rec-1")]);

        var result = GroundingOverlapCheck.Evaluate(claim, records);

        Assert.True(result.ClaimTokenCount > 0);
        Assert.True(result.IsSupported, $"ratio {result.Ratio}");
    }

    [Fact]
    public void A_run_mixing_latin_and_hangul_is_tokenized_by_each_script()
    {
        // "IoT플랫폼을" is one letter run; its Latin stretch is a word and its Hangul stretch pairs.
        var records = new[] { Record("rec-1", ("content", "세온시스템은 IoT 기반 예지보전 플랫폼을 만든다.")) };
        var claim = GroundedClaim.Create("세온시스템의 IoT플랫폼", sources: [new SourceRef("rec-1")]);

        var result = GroundingOverlapCheck.Evaluate(claim, records);

        Assert.Equal(result.ClaimTokenCount, result.MatchedTokenCount + 1); // only "템의" is not in the record
        Assert.True(result.IsSupported);
    }

    [Fact]
    public void A_claim_with_no_significant_token_is_not_supported()
    {
        var records = new[] { Record("rec-1", ("content", "a b 가")) };
        var claim = GroundedClaim.Create("a 가", sources: [new SourceRef("rec-1")]);

        var result = GroundingOverlapCheck.Evaluate(claim, records);

        Assert.Equal(0, result.ClaimTokenCount);
        Assert.False(result.IsSupported);
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
    [Fact]
    public void A_Korean_claim_is_supported_by_its_source_written_in_another_normalization_form()
    {
        // The record text arrives decomposed (NFD); the model quotes it precomposed. Same text, so the
        // character pairs must meet.
        var records = new[] { Record("r1", ("memo", "베어링 교체 작업을 완료했습니다".Normalize(NormalizationForm.FormD))) };
        var claim = GroundedClaim.Create("베어링 교체 작업 완료", sources: [new SourceRef("r1")]);

        Assert.True(GroundingOverlapCheck.IsSupportedBy(claim, records));
    }
}
