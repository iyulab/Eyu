using GilReview = Gil.Review;

namespace Eyu.Core.Linkage;

/// <summary>
/// One stratum of a clerical review sample: how many records the stratum holds in the population
/// it was drawn from, and the per-record scores of the records actually reviewed from it.
/// <para>
/// <paramref name="Name"/> is the caller's own label (the protocol's strata are gray-zone bands,
/// but nothing here depends on what they are) and appears in refusal messages, so a rejected
/// sample says which stratum was wrong rather than an index.
/// <paramref name="ReviewedScores"/> holds one score per reviewed record — B-cubed precision or
/// recall from <see cref="ClusteringMetrics.BCubedPerRecord"/>, one measure at a time, because
/// precision and recall are estimated separately.
/// </para>
/// </summary>
public sealed record ReviewStratum(string Name, long PopulationSize, IReadOnlyList<double> ReviewedScores);

/// <summary>The two ends of an interval, in the units of the score.</summary>
public sealed record ScoreInterval(double Low, double High);

/// <summary>
/// Why a <see cref="ClericalReviewEstimate"/> should be read with care. Set bits do not make the
/// numbers wrong — they say which of the estimator's assumptions did not hold — and the estimate
/// is still returned, because a caller comparing two reviews wants it either way. Same contract as
/// <see cref="LinkageErrorRateCaveat"/>.
/// </summary>
[Flags]
public enum ClericalReviewCaveat
{
    None = 0,

    /// <summary>
    /// A stratum was reviewed fewer than <see cref="ClericalReviewEstimator.MinimumReviewedPerStratum"/>
    /// times, so its sample variance is undefined. It is taken as zero, which makes the standard
    /// error an understatement rather than an error.
    /// </summary>
    StratumTooSmall = 1 << 0,

    /// <summary>
    /// A stratum was reviewed in full (<c>n_h == N_h</c>). Its finite-population correction is
    /// zero, so it contributes no sampling variance — correct, not a fault, but a reader comparing
    /// standard errors should know one stratum was a census.
    /// </summary>
    StratumIsCensus = 1 << 1,

    /// <summary>
    /// Every reviewed score in some stratum is identical — the boundary case the protocol warns
    /// about, where most records score 1.0 and a small sample sees no error at all. That is not
    /// evidence of no variance, only of little: the standard error then takes the stratum's variance
    /// from the most that <c>n_h</c> identical observations still allow
    /// (<see cref="ClericalReviewEstimator.UnobservedVariationBound"/>), so the normal interval
    /// stays as wide as the sample leaves the score uncertain. Resampling that stratum can only
    /// return the same mean, so the bootstrap interval is narrower than the uncertainty warrants —
    /// read <see cref="ClericalReviewEstimate.Normal"/> when this is set.
    /// </summary>
    NoVariationInStratum = 1 << 2,
}

/// <summary>
/// A population score estimated from a stratified clerical review, per
/// <c>docs/clerical-review.md</c> section 3 — the point estimate, its standard error, and the two
/// intervals the protocol requires, side by side.
/// <para>
/// Both intervals are reported because they disagree exactly where it matters: the scores are
/// bounded in [0, 1] and pile up at 1.0, so the normal approximation runs past the boundary while
/// the bootstrap does not. Neither is clamped into range — a normal interval reaching above 1.0 is
/// the signal that it should not be trusted here, and clamping would hide it. Prefer
/// <paramref name="Bootstrap"/> when the two disagree — unless the caveats include
/// <see cref="ClericalReviewCaveat.NoVariationInStratum"/>: a stratum whose reviewed scores are all
/// alike has nothing to resample, so its bootstrap collapses to a point, while the standard error
/// carries the bound that stratum's sample still allows. Both are built on the same design: the
/// bootstrap is the Rao–Wu rescaling bootstrap, which carries the finite-population correction the
/// standard error carries, so where they differ it is the skew talking and not the method.
/// </para>
/// <para>
/// This is a measurement, unlike <see cref="LinkageErrorRateEstimate"/>, which infers error rates
/// from a fitted model with no labels at all. The weights that make it one are design weights:
/// they come from the population as the pipeline clustered it, so a different configuration needs
/// a new sample rather than a re-weighting of this one.
/// </para>
/// </summary>
public sealed record ClericalReviewEstimate(
    double Estimate,
    double StandardError,
    ScoreInterval Normal,
    ScoreInterval Bootstrap,
    int SampleSize,
    long PopulationSize,
    int Seed,
    ClericalReviewCaveat Caveats)
{
    /// <summary>True when every assumption behind the estimate held for this sample.</summary>
    public bool IsReliable => Caveats == ClericalReviewCaveat.None;
}

/// <summary>
/// Turns the per-record scores of a stratified review sample into a population estimate with an
/// interval — the arithmetic of <c>docs/clerical-review.md</c> section 3, so that the protocol is
/// executable rather than only written down.
/// </summary>
public static class ClericalReviewEstimator
{
    /// <summary>Draws in the bootstrap, as the protocol specifies.</summary>
    public const int BootstrapDraws = 2000;

    /// <summary>The two-sided 95% normal quantile the protocol's interval uses.</summary>
    public const double NormalQuantile95 = 1.96;

    /// <summary>Below this many reviewed records a stratum has no sample variance to contribute.</summary>
    public const int MinimumReviewedPerStratum = 2;

    /// <summary>
    /// The variance a stratum is given when every one of its <paramref name="reviewed"/> scores is
    /// the same: <c>p (1 − p)</c> with <c>p = 1 − 0.025^(1/n_h)</c>, the exact (Clopper–Pearson)
    /// upper bound, at the interval's two-sided 95% level, on the fraction of the stratum that
    /// could score differently when none of <c>n_h</c> reviewed records did. A score in [0, 1]
    /// whose values differ on a fraction <c>p</c> of records has variance at most <c>p (1 − p)</c>
    /// (Popoviciu's inequality on a two-valued split), so the bound holds for fractional B-cubed
    /// scores without treating them as right-or-wrong.
    /// <para>
    /// Without it a stratum that happened to show no error contributed a variance of exactly zero,
    /// and the "95%" interval collapsed onto the observed score: on a labeled benchmark, where rare
    /// errors in a large stratum were missed by a 30-record pilot, that interval contained the
    /// census recall 4 times in 100. Thirty identical scores do not say the stratum has no errors;
    /// they say it has fewer than about one in eight.
    /// </para>
    /// </summary>
    public static double UnobservedVariationBound(int reviewed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(reviewed, 1);
        return GilReview.ReviewEstimator.UnobservedVariationBound(reviewed);
    }

    /// <summary>
    /// <paramref name="seed"/> is required rather than optional: the protocol's report names the
    /// draw method and seed on its Sample line, so an estimate nobody can reproduce does not
    /// satisfy it.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// When the sample cannot describe the population it claims to: no strata at all, a stratum
    /// with a non-positive population, an unsampled stratum (its mean is undefined, and weighting
    /// the others up in its place would silently estimate a different population), or more
    /// reviewed records than the stratum holds. Also when a stratum has no name or shares one (as
    /// <see cref="ClericalReviewSampler.DrawPilot(IReadOnlyList{PopulationStratum}, int, int)"/>
    /// refuses), or a score is not a proportion in [0, 1].
    /// </exception>
    public static ClericalReviewEstimate Estimate(IReadOnlyList<ReviewStratum> strata, int seed)
    {
        ArgumentNullException.ThrowIfNull(strata);
        if (strata.Count == 0)
        {
            throw new ArgumentException("A stratified estimate needs at least one stratum.", nameof(strata));
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var stratum in strata)
        {
            if (string.IsNullOrEmpty(stratum.Name) || !names.Add(stratum.Name))
            {
                throw new ArgumentException($"Every stratum needs a name of its own; '{stratum.Name}' is empty or given twice.", nameof(strata));
            }

            foreach (var score in stratum.ReviewedScores)
            {
                if (!(score >= 0.0 && score <= 1.0))
                {
                    throw new ArgumentException(
                        $"Stratum '{stratum.Name}' holds a score of {score}; a reviewed score is a proportion in [0, 1].",
                        nameof(strata));
                }
            }

            if (stratum.PopulationSize <= 0)
            {
                throw new ArgumentException(
                    $"Stratum '{stratum.Name}' claims a population of {stratum.PopulationSize}; a stratum holding no records cannot be weighted in.",
                    nameof(strata));
            }

            if (stratum.ReviewedScores.Count == 0)
            {
                throw new ArgumentException(
                    $"Stratum '{stratum.Name}' holds {stratum.PopulationSize} records but has no scored record -- none was reviewed, or every reviewed one was excluded as unresolvable. Its mean is undefined, and leaving it out would estimate a different population than the one described.",
                    nameof(strata));
            }

            if (stratum.ReviewedScores.Count > stratum.PopulationSize)
            {
                throw new ArgumentException(
                    $"Stratum '{stratum.Name}' reviewed {stratum.ReviewedScores.Count} records out of a population of {stratum.PopulationSize}.",
                    nameof(strata));
            }
        }

        // Estimated by Gil.Review. Every reviewed score becomes one item of weight 1 in its
        // stratum's index. The checks above are Eyu's own, so this API's exceptions and messages do
        // not depend on Gil's. The int seed is sign-extended to ulong.
        var items = new List<GilReview.ScoredItem>();
        for (var s = 0; s < strata.Count; s++)
        {
            foreach (var score in strata[s].ReviewedScores)
            {
                items.Add(new GilReview.ScoredItem(s, score, 1.0));
            }
        }

        var estimate = GilReview.ReviewEstimator.Estimate(
            [.. strata.Select(stratum => new GilReview.Stratum(stratum.Name, stratum.PopulationSize))],
            items,
            BootstrapDraws,
            unchecked((ulong)seed));

        // WeightsDegenerate (1 << 3) cannot arise with unit weights (Kish's effective size is n) and
        // has no Eyu counterpart; the other three bits share Eyu's values.
        const GilReview.EstimateCaveats shared =
            GilReview.EstimateCaveats.StratumTooSmall | GilReview.EstimateCaveats.StratumIsCensus | GilReview.EstimateCaveats.NoVariationInStratum;

        return new ClericalReviewEstimate(
            estimate.Point,
            estimate.StandardError,
            new ScoreInterval(estimate.Normal.Low, estimate.Normal.High),
            new ScoreInterval(estimate.Bootstrap.Low, estimate.Bootstrap.High),
            checked((int)estimate.SampleSize),
            estimate.Population,
            seed,
            (ClericalReviewCaveat)(int)(estimate.Caveats & shared));
    }
}
