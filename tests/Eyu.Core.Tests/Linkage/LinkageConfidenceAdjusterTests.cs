using Eyu.Core.Linkage;
using Eyu.Core.Records;
using Xunit;

namespace Eyu.Core.Tests.Linkage;

public class LinkageConfidenceAdjusterTests
{
    private static readonly FieldLinkageParameters NeutralParameters = new(
        new Dictionary<string, double>(), new Dictionary<string, double>(), 0.5, EstimationStatus.HeuristicDefault);

    private static LinkageAnalysis Analysis(params PairLinkage[] pairLinkages) =>
        new(new ClusteringResult([], []), pairLinkages, NeutralParameters);

    [Fact]
    public void A_claim_citing_zero_or_one_record_is_returned_unchanged()
    {
        var analysis = Analysis();

        Assert.Equal(0.42, LinkageConfidenceAdjuster.AdjustConfidence([], 0.42, analysis));
        Assert.Equal(0.42, LinkageConfidenceAdjuster.AdjustConfidence(["rec-1"], 0.42, analysis));
    }

    [Fact]
    public void A_claim_whose_records_have_no_recorded_pair_linkage_is_returned_unchanged()
    {
        var analysis = Analysis();

        Assert.Equal(0.42, LinkageConfidenceAdjuster.AdjustConfidence(["rec-1", "rec-2"], 0.42, analysis));
    }

    [Fact]
    public void A_confirmed_Match_ignores_the_LLM_confidence_entirely_and_uses_the_FS_probability()
    {
        var analysis = Analysis(new PairLinkage("rec-1", "rec-2", LinkageClassification.Match, 5.0));

        var adjusted = LinkageConfidenceAdjuster.AdjustConfidence(["rec-1", "rec-2"], llmConfidence: 0.1, analysis);

        var expected = 1.0 / (1.0 + Math.Exp(-5.0));
        Assert.Equal(expected, adjusted, precision: 6);
    }

    [Fact]
    public void A_zero_prior_gray_zone_pair_collapses_the_posterior_to_the_LLM_confidence()
    {
        var analysis = Analysis(new PairLinkage("rec-1", "rec-2", LinkageClassification.GrayZone, 0.0));

        var adjusted = LinkageConfidenceAdjuster.AdjustConfidence(["rec-1", "rec-2"], llmConfidence: 0.9, analysis);

        Assert.Equal(0.9, adjusted, precision: 6);
    }

    [Fact]
    public void A_negative_prior_gray_zone_pair_pulls_the_posterior_below_the_LLM_confidence()
    {
        var analysis = Analysis(new PairLinkage("rec-1", "rec-2", LinkageClassification.GrayZone, -1.0));

        var adjusted = LinkageConfidenceAdjuster.AdjustConfidence(["rec-1", "rec-2"], llmConfidence: 0.9, analysis);

        Assert.True(adjusted < 0.9);
    }

    [Fact]
    public void The_record_id_order_of_the_pair_linkage_does_not_matter()
    {
        var analysis = Analysis(new PairLinkage("rec-2", "rec-1", LinkageClassification.Match, 5.0));

        var adjusted = LinkageConfidenceAdjuster.AdjustConfidence(["rec-1", "rec-2"], llmConfidence: 0.1, analysis);

        var expected = 1.0 / (1.0 + Math.Exp(-5.0));
        Assert.Equal(expected, adjusted, precision: 6);
    }

    [Fact]
    public void A_confirmed_multi_record_cluster_uses_FS_confidence_even_when_a_transitive_pair_reads_GrayZone()
    {
        var records = new[]
        {
            new RawRecord("a", new Dictionary<string, string?> { ["name"] = "Acme", ["city"] = "Springfield" }),
            new RawRecord("b", new Dictionary<string, string?> { ["name"] = "Acme", ["city"] = "Springfield", ["phone"] = "555-0100" }),
            new RawRecord("c", new Dictionary<string, string?> { ["name"] = "Acme", ["phone"] = "555-0100" }),
        };
        var analysis = LinkagePipeline.Analyze(records);

        var adjusted = LinkageConfidenceAdjuster.AdjustConfidence(["a", "b", "c"], llmConfidence: 0.5, analysis);

        // a-b and b-c are direct Match edges (2 agreeing fields each); a-c is GrayZone (only "name"
        // shared) but a/b/c still end up in one EntityClusterer cluster via the a-b/b-c chain. The
        // fix must use the FS-derived probability from the direct Match edges, NOT blend in the
        // model's 0.5 confidence just because a-c individually reads GrayZone.
        var expectedLlr = 2 * Math.Log(0.9 / 0.1);
        var expected = 1.0 / (1.0 + Math.Exp(-expectedLlr));
        Assert.Equal(expected, adjusted, precision: 6);
        Assert.NotEqual(0.9, adjusted);
    }

    [Fact]
    public void An_out_of_range_LLM_confidence_throws_even_when_multiple_records_are_cited()
    {
        var analysis = Analysis(new PairLinkage("rec-1", "rec-2", LinkageClassification.GrayZone, 0.0));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LinkageConfidenceAdjuster.AdjustConfidence(["rec-1", "rec-2"], llmConfidence: 1.5, analysis));
    }

    [Fact]
    public void A_non_neutral_MatchPrior_shifts_a_confirmed_Matchs_confidence_below_the_prior_naive_value()
    {
        var lowPriorParameters = new FieldLinkageParameters(
            new Dictionary<string, double>(), new Dictionary<string, double>(), 0.1, EstimationStatus.Converged);
        var analysis = new LinkageAnalysis(
            new ClusteringResult([new RecordCluster(["rec-1", "rec-2"])], []),
            [new PairLinkage("rec-1", "rec-2", LinkageClassification.Match, 5.0)],
            lowPriorParameters);

        var adjusted = LinkageConfidenceAdjuster.AdjustConfidence(["rec-1", "rec-2"], llmConfidence: 0.1, analysis);

        var expectedLogOdds = 5.0 + Math.Log(0.1 / 0.9);
        var expected = 1.0 / (1.0 + Math.Exp(-expectedLogOdds));
        Assert.Equal(expected, adjusted, precision: 6);
        // A lower match prior (fewer true matches expected a priori) must pull confidence below
        // the prior-naive Sigmoid(5.0) a caller ignoring MatchPrior entirely would have returned.
        var priorNaiveConfidence = 1.0 / (1.0 + Math.Exp(-5.0));
        Assert.True(adjusted < priorNaiveConfidence);
    }

    [Fact]
    public void A_non_neutral_MatchPrior_shifts_the_Bayesian_combine_branchs_posterior()
    {
        var lowPriorParameters = new FieldLinkageParameters(
            new Dictionary<string, double>(), new Dictionary<string, double>(), 0.1, EstimationStatus.Converged);
        var analysis = new LinkageAnalysis(
            new ClusteringResult([], []),
            [new PairLinkage("rec-1", "rec-2", LinkageClassification.GrayZone, 0.0)],
            lowPriorParameters);

        var adjusted = LinkageConfidenceAdjuster.AdjustConfidence(["rec-1", "rec-2"], llmConfidence: 0.9, analysis);

        // priorLogOdds = 0.0 (pair LLR) + Logit(0.1) = -ln(9); llmLogOdds = Logit(0.9) = +ln(9);
        // these cancel exactly, so Sigmoid(0) = 0.5 -- a clean, hand-verifiable expected value that
        // could only come out this way if matchPriorLogOdds is actually being added in this branch
        // (with the neutral MatchPrior=0.5 fixture, this same setup collapses to 0.9 instead -- see
        // A_zero_prior_gray_zone_pair_collapses_the_posterior_to_the_LLM_confidence).
        Assert.Equal(0.5, adjusted, precision: 6);
    }
}
