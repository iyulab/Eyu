using Eyu.Core.Linkage;
using Xunit;

namespace Eyu.Core.Tests.Linkage;

public class ClericalReviewEstimatorTests
{
    private const int Seed = 20260912;

    // Small enough to check by hand, which is the point: the estimator is arithmetic out of
    // docs/clerical-review.md section 3, and arithmetic nobody recomputed is arithmetic nobody
    // checked. Two strata of 100 records, two reviewed from each.
    //
    //   A: scores 1.0, 0.5  ->  mean 0.75,  sample variance 0.125
    //   B: scores 0.8, 0.6  ->  mean 0.70,  sample variance 0.02
    //   weights 100/200 = 0.5 each
    //   estimate = 0.5*0.75 + 0.5*0.70                              = 0.725
    //   SE^2     = 0.25*(1-2/100)*0.125/2 + 0.25*(1-2/100)*0.02/2   = 0.0177625
    //   SE       = sqrt(0.0177625)                                  = 0.13327602935...
    //   normal   = 0.725 +/- 1.96*SE                                = [0.4637790, 0.9862210]
    [Fact]
    public void The_estimate_its_standard_error_and_its_normal_interval_match_the_protocols_formula()
    {
        var estimate = ClericalReviewEstimator.Estimate(
            [
                new ReviewStratum("A", 100, [1.0, 0.5]),
                new ReviewStratum("B", 100, [0.8, 0.6]),
            ],
            Seed);

        Assert.Equal(0.725, estimate.Estimate, 12);
        Assert.Equal(0.0177625, estimate.StandardError * estimate.StandardError, 12);
        Assert.Equal(0.1332760293526184, estimate.StandardError, 12);
        Assert.Equal(0.4637789824688679, estimate.Normal.Low, 9);
        Assert.Equal(0.9862210175311320, estimate.Normal.High, 9);
        Assert.Equal(4, estimate.SampleSize);
        Assert.Equal(200L, estimate.PopulationSize);
        Assert.Equal(ClericalReviewCaveat.None, estimate.Caveats);
        Assert.True(estimate.IsReliable);
    }

    // The seed is on the record because the protocol's report has to name it. An estimate whose
    // interval moves between two runs of the same sample is not a number anyone can cite.
    [Fact]
    public void The_same_sample_and_seed_give_the_same_bootstrap_interval()
    {
        ReviewStratum[] Sample() =>
        [
            new ReviewStratum("A", 100, [1.0, 0.5, 0.9, 0.4]),
            new ReviewStratum("B", 300, [0.8, 0.6, 1.0, 0.7]),
        ];

        var first = ClericalReviewEstimator.Estimate(Sample(), Seed);
        var second = ClericalReviewEstimator.Estimate(Sample(), Seed);
        var other = ClericalReviewEstimator.Estimate(Sample(), Seed + 1);

        Assert.Equal(first.Bootstrap, second.Bootstrap);
        Assert.Equal(Seed, first.Seed);
        Assert.NotEqual(first.Bootstrap, other.Bootstrap);
    }

    // Weights are the population's, not the sample's. Reviewing both strata equally while one is
    // three times the size of the other must land nearer that stratum's mean -- getting this
    // backwards is the classic stratified-sampling error, so it is pinned.
    [Fact]
    public void Strata_are_weighted_by_the_population_they_stand_for_not_by_how_much_was_reviewed()
    {
        var estimate = ClericalReviewEstimator.Estimate(
            [
                new ReviewStratum("small", 100, [0.0, 0.0]),
                new ReviewStratum("large", 300, [1.0, 1.0]),
            ],
            Seed);

        Assert.Equal(0.75, estimate.Estimate, 12);
    }

    // The boundary case the protocol names: when every reviewed record scores 1.0, resampling can
    // only ever return 1.0, so the bootstrap reports a point and the normal interval reports
    // nothing either. Both are honest about the sample and both understate the uncertainty -- the
    // caveat is what says so.
    [Fact]
    public void A_stratum_whose_scores_never_vary_is_flagged_rather_than_reported_as_certainty()
    {
        var estimate = ClericalReviewEstimator.Estimate(
            [new ReviewStratum("all-correct", 500, [1.0, 1.0, 1.0, 1.0])],
            Seed);

        Assert.Equal(1.0, estimate.Estimate, 12);
        Assert.Equal(1.0, estimate.Bootstrap.Low, 12);
        Assert.Equal(1.0, estimate.Bootstrap.High, 12);
        Assert.Equal(ClericalReviewCaveat.NoVariationInStratum, estimate.Caveats);
        Assert.False(estimate.IsReliable);
    }

    // A stratum reviewed in full has no sampling variance left to estimate -- that is correct, not
    // a fault, but a reader comparing standard errors needs to know one stratum was a census.
    [Fact]
    public void A_stratum_reviewed_in_full_contributes_no_sampling_variance_and_says_so()
    {
        var estimate = ClericalReviewEstimator.Estimate(
            [new ReviewStratum("census", 3, [1.0, 0.5, 0.0])],
            Seed);

        Assert.Equal(0.0, estimate.StandardError, 12);
        Assert.True(estimate.Caveats.HasFlag(ClericalReviewCaveat.StratumIsCensus));
    }

    // One observation has no sample variance. Taking it as zero understates the standard error, so
    // the estimate is returned with the reason attached rather than silently looking precise.
    [Fact]
    public void A_stratum_reviewed_once_is_flagged_as_too_small_to_vary()
    {
        var estimate = ClericalReviewEstimator.Estimate(
            [
                new ReviewStratum("thin", 100, [0.5]),
                new ReviewStratum("ordinary", 100, [0.8, 0.6]),
            ],
            Seed);

        Assert.True(estimate.Caveats.HasFlag(ClericalReviewCaveat.StratumTooSmall));
        Assert.False(estimate.IsReliable);
    }

    // An unsampled stratum has no mean, and quietly dropping it would estimate a population that
    // is not the one described. Refused by name, the way a clustering that does not cover the same
    // records is refused.
    [Fact]
    public void A_stratum_with_a_population_but_no_reviewed_records_is_refused_by_name()
    {
        var error = Assert.Throws<ArgumentException>(() => ClericalReviewEstimator.Estimate(
            [
                new ReviewStratum("reviewed", 100, [0.5, 0.9]),
                new ReviewStratum("skipped", 400, []),
            ],
            Seed));

        Assert.Contains("skipped", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reviewing_more_records_than_a_stratum_holds_is_refused_by_name()
    {
        var error = Assert.Throws<ArgumentException>(() => ClericalReviewEstimator.Estimate(
            [new ReviewStratum("impossible", 2, [1.0, 1.0, 1.0])],
            Seed));

        Assert.Contains("impossible", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_sample_is_refused()
    {
        Assert.Throws<ArgumentException>(() => ClericalReviewEstimator.Estimate([], Seed));
    }

    // The intervals are left unclamped on purpose: scores live in [0, 1], and a normal interval
    // that runs past 1.0 is the visible sign that the approximation has failed at the boundary.
    // Clamping it to 1.0 would turn that warning into a plausible-looking number.
    [Fact]
    public void A_normal_interval_that_runs_past_the_score_range_is_left_visible()
    {
        var estimate = ClericalReviewEstimator.Estimate(
            [new ReviewStratum("near-ceiling", 1000, [1.0, 1.0, 1.0, 0.5])],
            Seed);

        Assert.True(estimate.Normal.High > 1.0, $"normal high was {estimate.Normal.High}");
        Assert.True(estimate.Bootstrap.High <= 1.0, $"bootstrap high was {estimate.Bootstrap.High}");
    }
}
