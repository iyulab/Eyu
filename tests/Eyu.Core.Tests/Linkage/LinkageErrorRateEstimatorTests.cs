using Eyu.Core.Linkage;
using Eyu.Core.Records;
using Xunit;

namespace Eyu.Core.Tests.Linkage;

public class LinkageErrorRateEstimatorTests
{
    private static FieldLinkageParameters Parameters(EstimationStatus status, double prior = 0.5, bool swapped = false) =>
        new(
            new Dictionary<string, double> { ["name"] = 0.9 },
            new Dictionary<string, double> { ["name"] = 0.1 },
            prior,
            status)
        { LabelsSwapped = swapped };

    private static PairLinkage Pair(int i, LinkageClassification classification, double llr) =>
        new($"a{i}", $"b{i}", classification, llr);

    [Fact]
    public void A_cleanly_separated_batch_reports_near_zero_error_rates_with_no_caveats()
    {
        // Ten confident matches and ten confident non-matches: every posterior sits at one end.
        var pairs = Enumerable.Range(0, 10).Select(i => Pair(i, LinkageClassification.Match, 12.0))
            .Concat(Enumerable.Range(10, 10).Select(i => Pair(i, LinkageClassification.NonMatch, -12.0)))
            .ToList();

        var estimate = LinkageErrorRateEstimator.Estimate(pairs, Parameters(EstimationStatus.Converged));

        Assert.True(estimate.IsReliable, estimate.Caveats.ToString());
        Assert.InRange(estimate.FalseMatchRate, 0.0, 0.001);
        Assert.InRange(estimate.FalseNonMatchRate, 0.0, 0.001);
        Assert.Equal(0.0, estimate.GrayZoneShare);
        Assert.InRange(estimate.ExpectedMatches, 9.99, 10.01);
        Assert.InRange(estimate.ExpectedNonMatches, 9.99, 10.01);
    }

    [Fact]
    public void A_match_called_on_weak_evidence_is_counted_as_an_expected_false_match()
    {
        // Same as above, but one pair was classified Match on a log-likelihood ratio the model
        // itself only gives a ~0.73 posterior -- so ~0.27 of one expected non-match was called a
        // match, out of ~10.27 expected non-matches overall.
        var pairs = Enumerable.Range(0, 9).Select(i => Pair(i, LinkageClassification.Match, 12.0))
            .Append(Pair(9, LinkageClassification.Match, 1.0))
            .Concat(Enumerable.Range(10, 10).Select(i => Pair(i, LinkageClassification.NonMatch, -12.0)))
            .ToList();

        var estimate = LinkageErrorRateEstimator.Estimate(pairs, Parameters(EstimationStatus.Converged));

        Assert.InRange(estimate.FalseMatchRate, 0.02, 0.04);
        Assert.InRange(estimate.FalseNonMatchRate, 0.0, 0.001);
    }

    [Fact]
    public void The_prior_is_part_of_the_posterior_not_just_the_likelihood_ratio()
    {
        // The same pair under a low match prior is a less likely match, so calling it Match costs
        // more expected false matches than under an even prior.
        var pairs = new[] { Pair(0, LinkageClassification.Match, 2.0), Pair(1, LinkageClassification.NonMatch, -12.0) };

        var even = LinkageErrorRateEstimator.Estimate(pairs, Parameters(EstimationStatus.Converged, prior: 0.5));
        var rare = LinkageErrorRateEstimator.Estimate(pairs, Parameters(EstimationStatus.Converged, prior: 0.05));

        Assert.True(rare.FalseMatchRate > even.FalseMatchRate);
    }

    [Fact]
    public void Gray_zone_pairs_are_reported_as_a_share_and_are_neither_error()
    {
        var pairs = new[]
        {
            Pair(0, LinkageClassification.Match, 12.0),
            Pair(1, LinkageClassification.GrayZone, 0.5),
            Pair(2, LinkageClassification.GrayZone, -0.5),
            Pair(3, LinkageClassification.NonMatch, -12.0),
        };

        var estimate = LinkageErrorRateEstimator.Estimate(pairs, Parameters(EstimationStatus.Converged));

        Assert.Equal(0.5, estimate.GrayZoneShare);
        Assert.InRange(estimate.FalseMatchRate, 0.0, 0.001);
        Assert.InRange(estimate.FalseNonMatchRate, 0.0, 0.001);
    }

    [Fact]
    public void Heuristic_parameters_and_too_few_pairs_are_both_named_as_caveats()
    {
        var pairs = new[] { Pair(0, LinkageClassification.Match, 12.0), Pair(1, LinkageClassification.NonMatch, -12.0) };

        var estimate = LinkageErrorRateEstimator.Estimate(pairs, Parameters(EstimationStatus.HeuristicDefault));

        Assert.False(estimate.IsReliable);
        Assert.True(estimate.Caveats.HasFlag(LinkageErrorRateCaveat.HeuristicParameters));
        Assert.True(estimate.Caveats.HasFlag(LinkageErrorRateCaveat.TooFewPairs));
        // The rates are still there for a caller comparing runs; the caveats say what they are worth.
        Assert.InRange(estimate.FalseMatchRate, 0.0, 0.001);
    }

    [Fact]
    public void A_batch_the_model_cannot_separate_is_flagged_rather_than_reported_as_accurate()
    {
        // Every pair sits at an ambiguous posterior: the naive false-match rate would read as small
        // only because nothing was called Match, and that is not accuracy.
        var pairs = Enumerable.Range(0, 8).Select(i => Pair(i, LinkageClassification.GrayZone, i % 2 == 0 ? 0.3 : -0.3)).ToList();

        var estimate = LinkageErrorRateEstimator.Estimate(pairs, Parameters(EstimationStatus.Converged));

        Assert.True(estimate.Caveats.HasFlag(LinkageErrorRateCaveat.PoorSeparation));
        Assert.False(estimate.IsReliable);
    }

    [Theory]
    [InlineData(EstimationStatus.NotConverged, false, LinkageErrorRateCaveat.NotConverged)]
    [InlineData(EstimationStatus.Converged, true, LinkageErrorRateCaveat.LabelsSwapped)]
    public void A_fit_that_was_not_clean_is_named(EstimationStatus status, bool swapped, LinkageErrorRateCaveat expected)
    {
        var pairs = Enumerable.Range(0, 5).Select(i => Pair(i, LinkageClassification.Match, 12.0))
            .Concat(Enumerable.Range(5, 5).Select(i => Pair(i, LinkageClassification.NonMatch, -12.0)))
            .ToList();

        var estimate = LinkageErrorRateEstimator.Estimate(pairs, Parameters(status, swapped: swapped));

        Assert.True(estimate.Caveats.HasFlag(expected));
        Assert.False(estimate.IsReliable);
    }

    [Fact]
    public void The_pipeline_exposes_the_estimate_beside_the_parameters_and_omits_it_when_nothing_was_compared()
    {
        var alone = LinkagePipeline.Analyze([new RawRecord("r1", new Dictionary<string, string?> { ["name"] = "Alpha" })]);
        Assert.Null(alone.Parameters);
        Assert.Null(alone.ErrorRates);

        var records = Enumerable.Range(0, 6)
            .Select(i => new RawRecord($"r{i}", new Dictionary<string, string?> { ["name"] = i < 3 ? "Alpha" : $"Beta {i}" }))
            .ToList();
        var analysis = LinkagePipeline.Analyze(records);

        Assert.NotNull(analysis.Parameters);
        Assert.NotNull(analysis.ErrorRates);
        Assert.Equal(analysis.PairLinkages.Count(p => p.Classification == LinkageClassification.GrayZone) / (double)analysis.PairLinkages.Count,
            analysis.ErrorRates!.GrayZoneShare);
    }
}
