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
    /// Every reviewed score in some stratum is identical, so resampling that stratum always
    /// returns the same mean. The bootstrap interval is then narrower than the uncertainty
    /// warrants — exactly the boundary case the protocol warns about, where most records score 1.0.
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
/// <paramref name="Bootstrap"/> when the two disagree.
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
                    $"Stratum '{stratum.Name}' holds {stratum.PopulationSize} records but none were reviewed. Its mean is undefined, and leaving it out would estimate a different population than the one described.",
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
            // variance, because nothing was left in it that was not looked at.
            var correction = 1.0 - ((double)reviewed / stratum.PopulationSize);
            varianceSum += weight * weight * correction * SampleVariance(stratum.ReviewedScores, mean) / reviewed;
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
    /// Percentile bootstrap: resample each stratum with replacement to its own reviewed size,
    /// recompute the weighted estimate, and take the 2.5th and 97.5th percentiles of the draws.
    /// Resampling happens <em>within</em> each stratum so the design is preserved — pooling the
    /// strata first would estimate a simple random sample, which this is not.
    /// </summary>
    private static ScoreInterval Bootstrap(IReadOnlyList<ReviewStratum> strata, long population, int seed)
    {
        var random = new Random(seed);
        var draws = new double[BootstrapDraws];
        for (var draw = 0; draw < BootstrapDraws; draw++)
        {
            double resampled = 0;
            foreach (var stratum in strata)
            {
                var scores = stratum.ReviewedScores;
                double sum = 0;
                for (var i = 0; i < scores.Count; i++)
                {
                    sum += scores[random.Next(scores.Count)];
                }

                resampled += (double)stratum.PopulationSize / population * (sum / scores.Count);
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
