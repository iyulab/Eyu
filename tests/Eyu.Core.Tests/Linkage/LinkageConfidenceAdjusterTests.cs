using Eyu.Core.Linkage;
using Xunit;

namespace Eyu.Core.Tests.Linkage;

public class LinkageConfidenceAdjusterTests
{
    private static LinkageAnalysis Analysis(params PairLinkage[] pairLinkages) =>
        new(new ClusteringResult([], []), pairLinkages);

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
}
