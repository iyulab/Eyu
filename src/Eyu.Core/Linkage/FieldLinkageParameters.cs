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
}
