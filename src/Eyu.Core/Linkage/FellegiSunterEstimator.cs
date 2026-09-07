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
    public const double HeuristicDefaultMAgreeProbability = 0.9;
    public const double HeuristicDefaultUAgreeProbability = 0.1;
    public const double HeuristicDefaultMatchPrior = 0.5;
    public const int DefaultMaxIterations = 100;
    public const double DefaultConvergenceTolerance = 1e-4;

    private const double ProbabilityFloor = 0.01;
    private const double ProbabilityCeiling = 0.99;

    public static FieldLinkageParameters Estimate(
        IReadOnlyList<IReadOnlyDictionary<string, FieldAgreementLevel>> comparisonVectors,
        int maxIterations = DefaultMaxIterations,
        double convergenceTolerance = DefaultConvergenceTolerance)
    {
        ArgumentNullException.ThrowIfNull(comparisonVectors);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxIterations, 1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(convergenceTolerance);

        var fieldNames = comparisonVectors.SelectMany(v => v.Keys).Distinct(StringComparer.Ordinal).ToList();

        if (comparisonVectors.Count < MinimumPairsForEmEstimation || fieldNames.Count == 0)
        {
            return new FieldLinkageParameters(
                fieldNames.ToDictionary(f => f, _ => HeuristicDefaultMAgreeProbability, StringComparer.Ordinal),
                fieldNames.ToDictionary(f => f, _ => HeuristicDefaultUAgreeProbability, StringComparer.Ordinal),
                HeuristicDefaultMatchPrior,
                EstimationStatus.HeuristicDefault);
        }

        var m = fieldNames.ToDictionary(f => f, _ => HeuristicDefaultMAgreeProbability, StringComparer.Ordinal);
        var u = fieldNames.ToDictionary(f => f, _ => HeuristicDefaultUAgreeProbability, StringComparer.Ordinal);
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
                    var agreesUnderMatch = m[field];
                    var agreesUnderNonMatch = u[field];
                    matchLikelihood *= level == FieldAgreementLevel.Agree ? agreesUnderMatch : 1.0 - agreesUnderMatch;
                    nonMatchLikelihood *= level == FieldAgreementLevel.Agree ? agreesUnderNonMatch : 1.0 - agreesUnderNonMatch;
                }

                var total = matchLikelihood + nonMatchLikelihood;
                responsibilities[i] = total <= 0.0 ? 0.0 : matchLikelihood / total;
            }

            var newM = new Dictionary<string, double>(StringComparer.Ordinal);
            var newU = new Dictionary<string, double>(StringComparer.Ordinal);

            foreach (var field in fieldNames)
            {
                double matchWeightSum = 0, matchAgreeSum = 0, nonMatchWeightSum = 0, nonMatchAgreeSum = 0;

                for (var i = 0; i < comparisonVectors.Count; i++)
                {
                    if (!comparisonVectors[i].TryGetValue(field, out var level))
                    {
                        continue;
                    }

                    var r = responsibilities[i];
                    matchWeightSum += r;
                    nonMatchWeightSum += 1.0 - r;
                    if (level == FieldAgreementLevel.Agree)
                    {
                        matchAgreeSum += r;
                        nonMatchAgreeSum += 1.0 - r;
                    }
                }

                newM[field] = matchWeightSum <= 0.0
                    ? m[field]
                    : Math.Clamp(matchAgreeSum / matchWeightSum, ProbabilityFloor, ProbabilityCeiling);
                newU[field] = nonMatchWeightSum <= 0.0
                    ? u[field]
                    : Math.Clamp(nonMatchAgreeSum / nonMatchWeightSum, ProbabilityFloor, ProbabilityCeiling);
            }

            var newMatchPrior = Math.Clamp(responsibilities.Average(), ProbabilityFloor, ProbabilityCeiling);
            var delta = fieldNames.Max(f => Math.Max(Math.Abs(newM[f] - m[f]), Math.Abs(newU[f] - u[f])));

            m = newM;
            u = newU;
            matchPrior = newMatchPrior;

            if (delta < convergenceTolerance)
            {
                converged = true;
                break;
            }
        }

        return WithMatchComponentFirst(
            new FieldLinkageParameters(m, u, matchPrior, converged ? EstimationStatus.Converged : EstimationStatus.NotConverged));
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
    /// to carry its negative weight.
    /// </summary>
    public static FieldLinkageParameters WithMatchComponentFirst(FieldLinkageParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var allAgreeLogLikelihoodRatio = parameters.MAgreeProbability.Keys
            .Where(parameters.UAgreeProbability.ContainsKey)
            .Sum(field => Math.Log(parameters.MAgreeProbability[field] / parameters.UAgreeProbability[field]));

        if (allAgreeLogLikelihoodRatio >= 0.0)
        {
            return parameters;
        }

        return new FieldLinkageParameters(
            parameters.UAgreeProbability,
            parameters.MAgreeProbability,
            1.0 - parameters.MatchPrior,
            parameters.Status)
        { LabelsSwapped = true };
    }

    public static double ComputeLogLikelihoodRatio(
        IReadOnlyDictionary<string, FieldAgreementLevel> comparisonVector,
        FieldLinkageParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(comparisonVector);
        ArgumentNullException.ThrowIfNull(parameters);

        var logLikelihoodRatio = 0.0;
        foreach (var (field, level) in comparisonVector)
        {
            if (!parameters.MAgreeProbability.TryGetValue(field, out var m) ||
                !parameters.UAgreeProbability.TryGetValue(field, out var u))
            {
                continue;
            }

            logLikelihoodRatio += level == FieldAgreementLevel.Agree
                ? Math.Log(m / u)
                : Math.Log((1.0 - m) / (1.0 - u));
        }

        return logLikelihoodRatio;
    }
}
