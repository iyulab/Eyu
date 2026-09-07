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
}
