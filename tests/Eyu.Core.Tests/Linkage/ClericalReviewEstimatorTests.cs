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
    // only ever return 1.0, so the bootstrap reports a point. The standard error does not: four
    // identical scores bound the fraction that could differ at 1 - 0.025^(1/4) ~ 0.602, and the
    // normal interval is as wide as that leaves the score. The caveat still says the sample saw no
    // variation -- the value is fixed, the warning is not silenced.
    [Fact]
    public void A_stratum_whose_scores_never_vary_keeps_the_uncertainty_its_sample_allows()
    {
        var estimate = ClericalReviewEstimator.Estimate(
            [new ReviewStratum("all-correct", 500, [1.0, 1.0, 1.0, 1.0])],
            Seed);

        var p = 1.0 - Math.Pow(0.025, 0.25);
        var expectedSe = Math.Sqrt((1.0 - (4.0 / 500)) * p * (1.0 - p) / 4);
        Assert.Equal(1.0, estimate.Estimate, 12);
        Assert.Equal(expectedSe, estimate.StandardError, 12);
        Assert.Equal(1.0 - (1.96 * expectedSe), estimate.Normal.Low, 12);
        Assert.True(estimate.Normal.High > 1.0);
        Assert.Equal(1.0, estimate.Bootstrap.Low, 12);
        Assert.Equal(1.0, estimate.Bootstrap.High, 12);
        Assert.Equal(ClericalReviewCaveat.NoVariationInStratum, estimate.Caveats);
        Assert.False(estimate.IsReliable);
    }

    // The bound shrinks as the sample grows -- thirty identical scores leave fewer than about one
    // record in eight possibly different -- and it is the bound that is used, not a fixed floor.
    [Theory]
    [InlineData(1, 0.975)]
    [InlineData(30, 0.11570)]
    [InlineData(300, 0.01222)]
    public void The_unobserved_variation_bound_is_the_exact_upper_bound_on_a_differing_fraction(int reviewed, double fraction)
    {
        Assert.Equal(fraction * (1 - fraction), ClericalReviewEstimator.UnobservedVariationBound(reviewed), 4);
    }

    // A stratum reviewed in full keeps contributing nothing even when its scores are all alike:
    // the bound is about what was not looked at, and in a census nothing was left.
    [Fact]
    public void A_census_stratum_contributes_no_variance_even_when_its_scores_never_vary()
    {
        var estimate = ClericalReviewEstimator.Estimate(
            [new ReviewStratum("census", 4, [1.0, 1.0, 1.0, 1.0])],
            Seed);

        Assert.Equal(0.0, estimate.StandardError, 12);
        Assert.True(estimate.Caveats.HasFlag(ClericalReviewCaveat.StratumIsCensus));
    }

    // A stratum reviewed in full has no sampling variance left to estimate -- that is correct, not
    // a fault, but a reader comparing standard errors needs to know one stratum was a census. The
    // bootstrap agrees: under the Rao-Wu rescaling a census stratum's draws collapse onto its mean,
    // where a plain resample would still scatter and the two intervals would disagree by design.
    [Fact]
    public void A_stratum_reviewed_in_full_contributes_no_sampling_variance_and_says_so()
    {
        var estimate = ClericalReviewEstimator.Estimate(
            [new ReviewStratum("census", 3, [1.0, 0.5, 0.0])],
            Seed);

        Assert.Equal(0.0, estimate.StandardError, 12);
        Assert.Equal(0.5, estimate.Bootstrap.Low, 12);
        Assert.Equal(0.5, estimate.Bootstrap.High, 12);
        Assert.True(estimate.Caveats.HasFlag(ClericalReviewCaveat.StratumIsCensus));
    }

    // The rescaling carries the finite-population correction, so the bootstrap's spread tracks the
    // standard error instead of the with-replacement variance: reviewing half a stratum halves the
    // variance, and the percentile interval should sit near the normal one rather than outside it.
    [Fact]
    public void The_bootstrap_interval_tracks_the_finite_population_standard_error()
    {
        var scores = Enumerable.Range(0, 40).Select(i => i % 4 == 0 ? 0.5 : 1.0).ToArray();   // mean 0.875
        var estimate = ClericalReviewEstimator.Estimate([new ReviewStratum("half-reviewed", 80, scores)], Seed);

        var normalHalfWidth = ClericalReviewEstimator.NormalQuantile95 * estimate.StandardError;
        var bootstrapHalfWidth = (estimate.Bootstrap.High - estimate.Bootstrap.Low) / 2.0;
        Assert.True(bootstrapHalfWidth > 0.7 * normalHalfWidth && bootstrapHalfWidth < 1.3 * normalHalfWidth,
            $"bootstrap half-width {bootstrapHalfWidth} vs normal {normalHalfWidth}");
        // A with-replacement resample of all 40 would be wider by 1/sqrt(1 - 40/80) = 1.41.
        Assert.True(bootstrapHalfWidth < 1.25 * normalHalfWidth, $"bootstrap half-width {bootstrapHalfWidth} ignores the correction");
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
