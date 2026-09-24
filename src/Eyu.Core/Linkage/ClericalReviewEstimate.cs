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
        var fraction = 1.0 - Math.Pow(0.025, 1.0 / reviewed);
        return fraction * (1.0 - fraction);
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
    /// reviewed records than the stratum holds.
    /// </exception>
    public static ClericalReviewEstimate Estimate(IReadOnlyList<ReviewStratum> strata, int seed)
    {
        ArgumentNullException.ThrowIfNull(strata);
        if (strata.Count == 0)
        {
            throw new ArgumentException("A stratified estimate needs at least one stratum.", nameof(strata));
        }

        foreach (var stratum in strata)
        {
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

        long population = 0;
        foreach (var stratum in strata)
        {
            population += stratum.PopulationSize;
        }

        var caveats = ClericalReviewCaveat.None;
        double estimate = 0, varianceSum = 0;
        var sampleSize = 0;
        foreach (var stratum in strata)
        {
            var reviewed = stratum.ReviewedScores.Count;
            sampleSize += reviewed;
            var weight = (double)stratum.PopulationSize / population;
            var mean = Mean(stratum.ReviewedScores);
            estimate += weight * mean;

            if (reviewed < MinimumReviewedPerStratum)
            {
                caveats |= ClericalReviewCaveat.StratumTooSmall;
            }

            if (reviewed == stratum.PopulationSize)
            {
                caveats |= ClericalReviewCaveat.StratumIsCensus;
            }

            if (IsConstant(stratum.ReviewedScores))
            {
                caveats |= ClericalReviewCaveat.NoVariationInStratum;
            }

            // Finite-population correction: a stratum reviewed in full contributes no sampling
            // variance, because nothing was left in it that was not looked at. A stratum whose
            // scores never varied contributes the most variance its sample still allows, not zero.
            var correction = 1.0 - ((double)reviewed / stratum.PopulationSize);
            var variance = IsConstant(stratum.ReviewedScores)
                ? UnobservedVariationBound(reviewed)
                : SampleVariance(stratum.ReviewedScores, mean);
            varianceSum += weight * weight * correction * variance / reviewed;
        }

        var standardError = Math.Sqrt(varianceSum);
        var normal = new ScoreInterval(
            estimate - (NormalQuantile95 * standardError),
            estimate + (NormalQuantile95 * standardError));

        return new ClericalReviewEstimate(
            estimate,
            standardError,
            normal,
            Bootstrap(strata, population, seed),
            sampleSize,
            population,
            seed,
            caveats);
    }

    /// <summary>
    /// Percentile bootstrap under the sampling design — the rescaling bootstrap of Rao and Wu
    /// (1988) for stratified sampling without replacement. Each stratum is resampled with
    /// replacement to <c>m_h = n_h − 1</c> draws and the resampled mean is pulled back toward the
    /// observed one by <c>λ_h = sqrt(m_h (1 − f_h) / (n_h − 1)) = sqrt(1 − n_h/N_h)</c>, so that
    /// the draws' variance is <c>(1 − f_h) s²_h / n_h</c> — the same quantity the normal standard
    /// error uses. A plain resample to <c>n_h</c> ignores the finite-population correction: a
    /// stratum reviewed in full would then still scatter, and the two intervals would disagree by
    /// design rather than because the scores are skewed. With the rescaling, a census stratum
    /// contributes exactly nothing, as it does to the standard error.
    /// <para>
    /// Resampling happens <em>within</em> each stratum so the design is preserved — pooling the
    /// strata first would estimate a simple random sample, which this is not. A stratum reviewed
    /// once has no draw to make (<c>m_h = 0</c>) and contributes its single score unchanged, which
    /// is the same understatement <see cref="ClericalReviewCaveat.StratumTooSmall"/> already
    /// reports for the standard error.
    /// </para>
    /// </summary>
    private static ScoreInterval Bootstrap(IReadOnlyList<ReviewStratum> strata, long population, int seed)
    {
        var random = new Random(seed);
        var means = new double[strata.Count];
        var rescale = new double[strata.Count];
        for (var s = 0; s < strata.Count; s++)
        {
            var stratum = strata[s];
            means[s] = Mean(stratum.ReviewedScores);
            var reviewed = stratum.ReviewedScores.Count;
            rescale[s] = reviewed < MinimumReviewedPerStratum
                ? 0.0
                : Math.Sqrt(1.0 - ((double)reviewed / stratum.PopulationSize));
        }

        var draws = new double[BootstrapDraws];
        for (var draw = 0; draw < BootstrapDraws; draw++)
        {
            double resampled = 0;
            for (var s = 0; s < strata.Count; s++)
            {
                var scores = strata[s].ReviewedScores;
                var weight = (double)strata[s].PopulationSize / population;
                var m = scores.Count - 1;
                if (m < 1 || rescale[s] == 0.0)
                {
                    resampled += weight * means[s];
                    continue;
                }

                double sum = 0;
                for (var i = 0; i < m; i++)
                {
                    sum += scores[random.Next(scores.Count)];
                }

                resampled += weight * (means[s] + (rescale[s] * ((sum / m) - means[s])));
            }

            draws[draw] = resampled;
        }

        Array.Sort(draws);
        return new ScoreInterval(Percentile(draws, 0.025), Percentile(draws, 0.975));
    }

    /// <summary>
    /// Nearest-rank on the sorted draws — with 2000 of them the choice of interpolation moves an
    /// end by less than one draw.
    /// </summary>
    private static double Percentile(double[] sorted, double quantile) =>
        sorted[(int)Math.Clamp(Math.Round(quantile * (sorted.Length - 1)), 0, sorted.Length - 1)];

    private static double Mean(IReadOnlyList<double> values)
    {
        double sum = 0;
        foreach (var value in values)
        {
            sum += value;
        }

        return sum / values.Count;
    }

    /// <summary>
    /// Sample variance (n-1). Undefined for a single observation, where it is taken as zero and
    /// reported as <see cref="ClericalReviewCaveat.StratumTooSmall"/>.
    /// </summary>
    private static double SampleVariance(IReadOnlyList<double> values, double mean)
    {
        if (values.Count < MinimumReviewedPerStratum)
        {
            return 0.0;
        }

        double sum = 0;
        foreach (var value in values)
        {
            sum += (value - mean) * (value - mean);
        }

        return sum / (values.Count - 1);
    }

    private static bool IsConstant(IReadOnlyList<double> values)
    {
        for (var i = 1; i < values.Count; i++)
        {
            if (values[i] != values[0])
            {
                return false;
            }
        }

        return true;
    }
}
