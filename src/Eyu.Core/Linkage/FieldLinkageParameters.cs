namespace Eyu.Core.Linkage;

/// <summary>
/// A Fellegi-Sunter model's per-field parameters: <paramref name="MAgreeProbability"/> is
/// P(field agrees | the pair is a match), <paramref name="UAgreeProbability"/> is P(field agrees
/// | the pair is not a match), and <paramref name="MatchPrior"/> is the estimated fraction of
/// compared pairs that are matches. Keyed by the same field names <see cref="FieldComparator"/>
/// produces. <paramref name="Status"/> says which regime produced these values — see
/// <see cref="EstimationStatus"/>.
/// </summary>
public sealed record FieldLinkageParameters(
    IReadOnlyDictionary<string, double> MAgreeProbability,
    IReadOnlyDictionary<string, double> UAgreeProbability,
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
    /// field's <c>u</c> as measured (<see cref="LinkagePipeline.EstimateNonMatchAgreement"/>), one
    /// <paramref name="matchAgreement"/> as every field's <c>m</c>, and <see cref="EstimationStatus.Supplied"/>.
    /// There is no default for <paramref name="matchAgreement"/> on purpose: it is how often a true
    /// duplicate still agrees on a field, a property of how the records were written, and between 0.90
    /// and 0.95 it decides whether two different people with one name are told apart — the data has to
    /// set it (a reviewed sample, or <see cref="FellegiSunterEstimator.Estimate"/> with <c>u</c> fixed over
    /// pairs where duplicates gather).
    /// </summary>
    /// <param name="nonMatchAgreement">Each field's <c>u</c>.</param>
    /// <param name="matchAgreement">Every field's <c>m</c>; inside (0, 1).</param>
    /// <param name="matchPrior">The match prior; only posteriors read it, the log-likelihood ratio does not.</param>
    public static FieldLinkageParameters FromNonMatchAgreement(
        IReadOnlyDictionary<string, double> nonMatchAgreement, double matchAgreement, double matchPrior)
    {
        ArgumentNullException.ThrowIfNull(nonMatchAgreement);
        if (matchAgreement is not (> 0.0 and < 1.0))
        {
            throw new ArgumentOutOfRangeException(nameof(matchAgreement), matchAgreement, "Must be strictly between 0 and 1.");
        }

        return new FieldLinkageParameters(
            nonMatchAgreement.Keys.ToDictionary(f => f, _ => matchAgreement, StringComparer.Ordinal),
            new Dictionary<string, double>(nonMatchAgreement, StringComparer.Ordinal),
            matchPrior,
            EstimationStatus.Supplied);
    }
}
