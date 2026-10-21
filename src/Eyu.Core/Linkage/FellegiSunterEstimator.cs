namespace Eyu.Core.Linkage;

/// <summary>
/// Estimates <see cref="FieldLinkageParameters"/> from a batch of comparison vectors via
/// expectation-maximization (no labeled match/non-match data required — design rationale
/// decision 3), and scores a single pair's comparison vector against those parameters as a
/// log-likelihood ratio. Below <see cref="MinimumPairsForEmEstimation"/> pairs, EM is not run at
/// all: too few pairs make it unstable, so a documented heuristic default is returned instead
/// (design's "에러 처리" — the realistic small-batch case, e.g. a 3-record call).
/// </summary>
public static class FellegiSunterEstimator
{
    public const int MinimumPairsForEmEstimation = 5;

    /// <summary>
    /// The fewest compared fields from which EM can identify the mixture at all. A two-class model of
    /// independent binary agreements is not identifiable from fewer than three indicators (Goodman
    /// 1974): with one field every pair reports a single agree/disagree bit, which fixes one
    /// proportion and leaves the match prior and the field's <c>m</c> and <c>u</c> free, so EM
    /// converges to one of the many fits that explain it equally well. The fit is still returned —
    /// the pipeline needs parameters to run — and <see cref="LinkageErrorRateEstimator.Estimate"/>
    /// reports it as <see cref="LinkageErrorRateCaveat.TooFewFields"/>.
    /// </summary>
    public const int MinimumFieldsForIdentification = 3;
    /// <summary>
    /// The default <c>m</c> without a fit: a match reaches a field's top level this often, the rest spread
    /// evenly below it.
    /// </summary>
    public const double HeuristicDefaultMAgreeProbability = 0.9;

    /// <summary>
    /// The default <c>u</c> without a fit: a non-match reaches some agreeing level this often, spread evenly
    /// over the agreeing levels, and disagrees otherwise.
    /// </summary>
    public const double HeuristicDefaultUAgreeProbability = 0.1;
    public const double HeuristicDefaultMatchPrior = 0.5;
    public const int DefaultMaxIterations = 100;
    public const double DefaultConvergenceTolerance = 1e-4;

    /// <summary>
    /// Bounds on every field's re-estimated <c>m</c> and <c>u</c> — each level's probability, which is then
    /// renormalized so a field's levels still sum to 1. They are an <em>evidence cap</em>
    /// more than a numerical guard: no single field's agreement can weigh more than
    /// <c>log(0.99 / 0.01) ≈ 4.6</c> nats, just past the default match threshold of 4, so one
    /// coincidental agreement on a near-unique value (a shared date of birth, a shared postcode
    /// and suburb) cannot on its own outvote everything else that disagrees. Fellegi–Sunter assumes
    /// the fields are independent and real address fields are not; measured on a labeled benchmark,
    /// letting <c>u</c> fall to its true 1e-4 range raised recall slightly and cut precision from
    /// 0.986 to 0.950 on names and addresses alone.
    /// </summary>
    public const double AgreementProbabilityFloor = 0.01;

    /// <inheritdoc cref="AgreementProbabilityFloor"/>
    public const double AgreementProbabilityCeiling = 0.99;

    /// <summary>
    /// Pseudo-count added to each side of the match prior's re-estimate — Jeffreys' prior,
    /// <c>Beta(½, ½)</c>: <c>(Σ responsibilities + ½) / (pairs + 1)</c>. It keeps the prior strictly
    /// inside (0, 1), which its logit needs, pulling hard toward ½ only when there are few pairs.
    /// The prior used to share the fixed 0.01 floor of <see cref="AgreementProbabilityFloor"/>, and
    /// that floor did not shrink with the batch: past about a hundred records a deduplication
    /// batch's true match prior is below 1%, so the fitted prior was the floor itself (0.01 against
    /// 0.001 on a 1,000-record benchmark) and every posterior — and the unlabeled error-rate
    /// estimate built from them — was pushed toward match by a factor of ten.
    /// </summary>
    public const double PriorPseudoCount = 0.5;

    /// <param name="comparisonVectors">
    /// One agreement vector per compared pair. A field's levels are fitted on the scale its levels carry
    /// (<see cref="FieldAgreementLevel.Count"/>), which must be the same in every vector.
    /// </param>
    /// <param name="maxIterations">The most EM rounds.</param>
    /// <param name="convergenceTolerance">The largest parameter move still read as converged.</param>
    /// <param name="fixedNonMatchAgreement">
    /// The <c>u</c> of each field — its level distribution among non-matches, on the field's scale — measured elsewhere, typically
    /// <see cref="LinkagePipeline.EstimateNonMatchAgreement"/> over random pairs of the population, which
    /// are nearly all non-matches, so <c>u</c> needs no EM. When given, those fields' <c>u</c> is held where
    /// it is and EM fits only <c>m</c> and the match prior; a field it does not name is fitted as usual.
    /// That is how <c>m</c> is learned from pairs where duplicates gather (pairs that share a blocking value)
    /// without the pairs' own skew also moving <c>u</c> — provided the non-matches among those pairs agree
    /// about as often as random pairs do. Pairs selected for looking alike break that: their non-matches
    /// agree far more than the pinned <c>u</c> says, and EM puts the excess into the match component
    /// (measured on candidate sets of entity-matching benchmarks: a fitted match prior of 0.86–0.99 where
    /// the true share was 0.09–0.25, and precision falling with it). The components are not relabeled
    /// afterwards: with <c>u</c> pinned, which component is the non-match one is already decided.
    /// </param>
    public static FieldLinkageParameters Estimate(
        IReadOnlyList<IReadOnlyDictionary<string, FieldAgreementLevel>> comparisonVectors,
        int maxIterations = DefaultMaxIterations,
        double convergenceTolerance = DefaultConvergenceTolerance,
        IReadOnlyDictionary<string, IReadOnlyList<double>>? fixedNonMatchAgreement = null)
    {
        ArgumentNullException.ThrowIfNull(comparisonVectors);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxIterations, 1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(convergenceTolerance);

        var levels = LevelCounts(comparisonVectors);
        var fieldNames = levels.Keys.ToList();

        bool IsFixed(string field) => fixedNonMatchAgreement is not null && fixedNonMatchAgreement.ContainsKey(field);
        double[] InitialU(string field)
        {
            if (!IsFixed(field))
            {
                return [.. FieldLinkageParameters.AgreeingSpread(levels[field], HeuristicDefaultUAgreeProbability)];
            }

            var supplied = fixedNonMatchAgreement![field];
            if (supplied.Count != levels[field])
            {
                throw new ArgumentException(
                    $"The fixed u of '{field}' has {supplied.Count} levels; its comparison vectors have {levels[field]}.", nameof(fixedNonMatchAgreement));
            }

            return [.. supplied];
        }

        double[] InitialM(string field) => [.. FieldLinkageParameters.TopWeighted(levels[field], HeuristicDefaultMAgreeProbability)];

        if (comparisonVectors.Count < MinimumPairsForEmEstimation || fieldNames.Count == 0)
        {
            return new FieldLinkageParameters(
                fieldNames.ToDictionary(f => f, f => (IReadOnlyList<double>)InitialM(f), StringComparer.Ordinal),
                fieldNames.ToDictionary(f => f, f => (IReadOnlyList<double>)InitialU(f), StringComparer.Ordinal),
                HeuristicDefaultMatchPrior,
                EstimationStatus.HeuristicDefault);
        }

        var m = fieldNames.ToDictionary(f => f, InitialM, StringComparer.Ordinal);
        var u = fieldNames.ToDictionary(f => f, InitialU, StringComparer.Ordinal);
        var matchPrior = HeuristicDefaultMatchPrior;
        var converged = false;

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            var responsibilities = new double[comparisonVectors.Count];
            for (var i = 0; i < comparisonVectors.Count; i++)
            {
                var matchLikelihood = matchPrior;
                var nonMatchLikelihood = 1.0 - matchPrior;

                foreach (var (field, level) in comparisonVectors[i])
                {
                    matchLikelihood *= m[field][level.Rank];
                    nonMatchLikelihood *= u[field][level.Rank];
                }

                var total = matchLikelihood + nonMatchLikelihood;
                responsibilities[i] = total <= 0.0 ? 0.0 : matchLikelihood / total;
            }

            var newM = new Dictionary<string, double[]>(StringComparer.Ordinal);
            var newU = new Dictionary<string, double[]>(StringComparer.Ordinal);
            var delta = 0.0;

            foreach (var field in fieldNames)
            {
                var matchByLevel = new double[levels[field]];
                var nonMatchByLevel = new double[levels[field]];
                double matchWeightSum = 0, nonMatchWeightSum = 0;

                for (var i = 0; i < comparisonVectors.Count; i++)
                {
                    if (!comparisonVectors[i].TryGetValue(field, out var level))
                    {
                        continue;
                    }

                    var r = responsibilities[i];
                    matchWeightSum += r;
                    nonMatchWeightSum += 1.0 - r;
                    matchByLevel[level.Rank] += r;
                    nonMatchByLevel[level.Rank] += 1.0 - r;
                }

                newM[field] = matchWeightSum <= 0.0 ? m[field] : Bounded(matchByLevel, matchWeightSum);
                newU[field] = nonMatchWeightSum <= 0.0 || IsFixed(field) ? u[field] : Bounded(nonMatchByLevel, nonMatchWeightSum);

                for (var k = 0; k < levels[field]; k++)
                {
                    delta = Math.Max(delta, Math.Max(Math.Abs(newM[field][k] - m[field][k]), Math.Abs(newU[field][k] - u[field][k])));
                }
            }

            var newMatchPrior = (responsibilities.Sum() + PriorPseudoCount) / (responsibilities.Length + (2 * PriorPseudoCount));

            m = newM;
            u = newU;
            matchPrior = newMatchPrior;

            if (delta < convergenceTolerance)
            {
                converged = true;
                break;
            }
        }

        var fitted = new FieldLinkageParameters(
            m.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<double>)kv.Value, StringComparer.Ordinal),
            u.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<double>)kv.Value, StringComparer.Ordinal),
            matchPrior,
            converged ? EstimationStatus.Converged : EstimationStatus.NotConverged);
        return fixedNonMatchAgreement is null ? WithMatchComponentFirst(fitted) : fitted;
    }

    /// <summary>
    /// Each field's level count, read off the vectors themselves; a field whose levels disagree on the
    /// scale was compared two different ways, and no single distribution describes it.
    /// </summary>
    private static Dictionary<string, int> LevelCounts(IReadOnlyList<IReadOnlyDictionary<string, FieldAgreementLevel>> comparisonVectors)
    {
        var levels = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var vector in comparisonVectors)
        {
            foreach (var (field, level) in vector)
            {
                if (levels.TryGetValue(field, out var count) && count != level.Count)
                {
                    throw new ArgumentException(
                        $"Field '{field}' is compared on {count} levels in one vector and {level.Count} in another; compare every pair the same way.",
                        nameof(comparisonVectors));
                }

                levels[field] = level.Count;
            }
        }

        return levels;
    }

    /// <summary>
    /// <paramref name="weights"/> as a distribution, each level held within
    /// [<see cref="AgreementProbabilityFloor"/>, <see cref="AgreementProbabilityCeiling"/>]. The agreeing
    /// levels are bounded and disagreement takes what is left, so a two-level field is exactly the classic
    /// clamp of <c>m</c> with <c>1 − m</c> beside it; only when what is left falls outside the bounds too is
    /// it bounded as well and the whole renormalized.
    /// </summary>
    internal static double[] Bounded(double[] weights, double total)
    {
        var bounded = new double[weights.Length];
        var agreeing = 0.0;
        for (var k = 1; k < weights.Length; k++)
        {
            bounded[k] = Math.Clamp(weights[k] / total, AgreementProbabilityFloor, AgreementProbabilityCeiling);
            agreeing += bounded[k];
        }

        bounded[0] = 1.0 - agreeing;
        if (bounded[0] is >= AgreementProbabilityFloor and <= AgreementProbabilityCeiling)
        {
            return bounded;
        }

        bounded[0] = Math.Clamp(bounded[0], AgreementProbabilityFloor, AgreementProbabilityCeiling);
        var sum = bounded.Sum();
        for (var k = 0; k < bounded.Length; k++)
        {
            bounded[k] /= sum;
        }

        return bounded;
    }

    /// <summary>
    /// Pins down which mixture component is the match component. A two-component mixture is
    /// symmetric under relabeling — <c>(m, u, p)</c> and <c>(u, m, 1 - p)</c> have identical
    /// likelihood — so EM may converge with the two swapped ("label switching"), and every
    /// log-likelihood ratio then carries the wrong sign: <see cref="LinkageClassification.Match"/>
    /// would mean non-match. The identifying constraint of Fellegi-Sunter is that agreement is
    /// evidence <em>for</em> a match, i.e. an all-agree comparison vector has a non-negative
    /// log-likelihood ratio; when the estimate violates that, the components are swapped back.
    /// The decision is made on the whole model, not per field: a single field whose agreement
    /// happens to favor non-match is a weak or inverted field, not a relabeled model, and is left
    /// to carry its negative weight. «All agree» is every field on its top level.
    /// </summary>
    public static FieldLinkageParameters WithMatchComponentFirst(FieldLinkageParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var allAgreeLogLikelihoodRatio = parameters.MLevelProbabilities.Keys
            .Where(parameters.ULevelProbabilities.ContainsKey)
            .Sum(field => Math.Log(parameters.MLevelProbabilities[field][^1] / parameters.ULevelProbabilities[field][^1]));

        if (allAgreeLogLikelihoodRatio >= 0.0)
        {
            return parameters;
        }

        return new FieldLinkageParameters(
            parameters.ULevelProbabilities,
            parameters.MLevelProbabilities,
            1.0 - parameters.MatchPrior,
            parameters.Status)
        { LabelsSwapped = true };
    }

    /// <summary>
    /// The pair's evidence for a match: the sum over its fields of <c>log(m[level] / u[level])</c>. A field
    /// the parameters do not name carries no evidence; a field named on another scale than the vector's
    /// was fitted under another comparison and is refused rather than read at the wrong level.
    /// </summary>
    public static double ComputeLogLikelihoodRatio(
        IReadOnlyDictionary<string, FieldAgreementLevel> comparisonVector,
        FieldLinkageParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(comparisonVector);
        ArgumentNullException.ThrowIfNull(parameters);

        var logLikelihoodRatio = 0.0;
        foreach (var (field, level) in comparisonVector)
        {
            if (!parameters.MLevelProbabilities.TryGetValue(field, out var m) ||
                !parameters.ULevelProbabilities.TryGetValue(field, out var u))
            {
                continue;
            }

            if (m.Count != level.Count || u.Count != level.Count)
            {
                throw new ArgumentException(
                    $"Field '{field}' was compared on {level.Count} levels but its parameters have {m.Count} (m) and {u.Count} (u); score with parameters fitted under the same comparison.",
                    nameof(comparisonVector));
            }

            logLikelihoodRatio += Math.Log(m[level.Rank] / u[level.Rank]);
        }

        return logLikelihoodRatio;
    }
}
