namespace Eyu.Core.Linkage;

/// <summary>
/// A Fellegi-Sunter model's per-field parameters. <paramref name="MLevelProbabilities"/> gives, for each
/// field, P(the pair lands on each agreement level | the pair is a match) and
/// <paramref name="ULevelProbabilities"/> the same given a non-match — one probability per level of the
/// field's comparison, indexed by <see cref="FieldAgreementLevel.Rank"/> (0 = disagreement, the last = the
/// closest), summing to 1. A two-level field's lists are <c>[1 − m, m]</c> and <c>[1 − u, u]</c>, where
/// <c>m</c> and <c>u</c> are the classic agreement probabilities. <paramref name="MatchPrior"/> is the
/// estimated fraction of compared pairs that are matches. Keyed by the same field names
/// <see cref="FieldComparator"/> produces. <paramref name="Status"/> says which regime produced these
/// values — see <see cref="EstimationStatus"/>.
/// </summary>
public sealed record FieldLinkageParameters(
    IReadOnlyDictionary<string, IReadOnlyList<double>> MLevelProbabilities,
    IReadOnlyDictionary<string, IReadOnlyList<double>> ULevelProbabilities,
    double MatchPrior,
    EstimationStatus Status)
{
    /// <summary>
    /// Strictly inside (0, 1): the adjuster takes its logit, and 0 or 1 would put an infinity —
    /// or, further on, a NaN that <c>Math.Clamp</c> passes through — into every posterior.
    /// </summary>
    public double MatchPrior { get; init; } = MatchPrior is > 0.0 and < 1.0
        ? MatchPrior
        : throw new ArgumentOutOfRangeException(nameof(MatchPrior), MatchPrior, "Must be strictly between 0 and 1.");

    /// <summary>
    /// True when the estimator found EM had converged with its two components the wrong way round
    /// — the component it was calling "match" agreed less than the one it was calling "non-match"
    /// — and swapped them back (see <see cref="FellegiSunterEstimator.WithMatchComponentFirst"/>).
    /// The values here are already in the right order; the flag is for a harness that wants to
    /// know the batch needed it. Never set on a <see cref="EstimationStatus.HeuristicDefault"/>.
    /// </summary>
    public bool LabelsSwapped { get; init; }

    /// <summary>
    /// Parameters for scoring new records against a population without fitting anything on them: each
    /// field's level distribution among non-matches as measured (<see cref="LinkagePipeline.EstimateNonMatchAgreement"/>),
    /// <paramref name="matchAgreement"/> as every field's probability of a true duplicate reaching the top
    /// level, and <see cref="EstimationStatus.Supplied"/>. The rest of a match's probability is spread evenly
    /// over the lower levels — for a two-level field that is <c>[1 − m, m]</c>, exactly the classic model;
    /// to let the data shape the lower levels, fit <c>m</c> instead (a reviewed sample, or
    /// <see cref="FellegiSunterEstimator.Estimate"/> with <c>u</c> fixed over pairs where duplicates gather).
    /// There is no default for <paramref name="matchAgreement"/> on purpose: it is how often a true
    /// duplicate still agrees on a field, a property of how the records were written, and between 0.90
    /// and 0.95 it decides whether two different people with one name are told apart.
    /// </summary>
    /// <param name="nonMatchAgreement">Each field's level distribution among non-matches.</param>
    /// <param name="matchAgreement">Every field's top-level probability among matches; inside (0, 1).</param>
    /// <param name="matchPrior">The match prior; only posteriors read it, the log-likelihood ratio does not.</param>
    public static FieldLinkageParameters FromNonMatchAgreement(
        IReadOnlyDictionary<string, IReadOnlyList<double>> nonMatchAgreement, double matchAgreement, double matchPrior)
    {
        ArgumentNullException.ThrowIfNull(nonMatchAgreement);
        if (matchAgreement is not (> 0.0 and < 1.0))
        {
            throw new ArgumentOutOfRangeException(nameof(matchAgreement), matchAgreement, "Must be strictly between 0 and 1.");
        }

        return new FieldLinkageParameters(
            nonMatchAgreement.ToDictionary(kv => kv.Key, kv => TopWeighted(kv.Value.Count, matchAgreement), StringComparer.Ordinal),
            nonMatchAgreement.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<double>)[.. kv.Value], StringComparer.Ordinal),
            matchPrior,
            EstimationStatus.Supplied);
    }

    /// <summary><paramref name="top"/> on the closest of <paramref name="levels"/>, the rest spread evenly below it.</summary>
    internal static IReadOnlyList<double> TopWeighted(int levels, double top)
    {
        var rest = (1.0 - top) / (levels - 1);
        var distribution = new double[levels];
        Array.Fill(distribution, rest);
        distribution[^1] = top;
        return distribution;
    }

    /// <summary>
    /// <paramref name="agreeing"/> spread evenly over the agreeing levels, disagreement taking the rest —
    /// <c>[1 − u, u]</c> for two levels.
    /// </summary>
    internal static IReadOnlyList<double> AgreeingSpread(int levels, double agreeing)
    {
        var distribution = new double[levels];
        Array.Fill(distribution, agreeing / (levels - 1));
        distribution[0] = 1.0 - agreeing;
        return distribution;
    }
}
