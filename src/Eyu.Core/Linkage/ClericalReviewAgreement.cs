using GilReview = Gil.Review;

namespace Eyu.Core.Linkage;

/// <summary>
/// How far two reviewers agreed on the candidates both of them judged — the reviewer-agreement
/// line of <c>docs/clerical-review.md</c> section 5, pooled over every double-reviewed pair rather
/// than reported per stratum, because a stratum's double-reviewed subsample is a handful of
/// verdicts and a coefficient over a handful says nothing.
/// <para>
/// Two chance-corrected coefficients are reported side by side because they disagree exactly
/// where this review lives. In the singleton and clean-merge strata nearly every verdict is
/// <see cref="ReviewOutcome.Same"/> or nearly every one is <see cref="ReviewOutcome.Different"/>,
/// and under that prevalence Cohen's κ collapses toward zero — or is undefined when both
/// reviewers used a single outcome — while observed agreement is near one. Gwet's AC1 (Gwet
/// 2008) corrects for chance without that paradox. Read κ where the outcomes are mixed and AC1
/// where they are not, and <paramref name="SpecificAgreement"/> to see which outcome the
/// disagreement is about.
/// </para>
/// </summary>
public sealed record ReviewerAgreement(
    int ComparedPairs,
    double ObservedAgreement,
    double CohenKappa,
    double GwetAc1,
    IReadOnlyDictionary<ReviewOutcome, double> SpecificAgreement);

/// <summary>
/// Computes <see cref="ReviewerAgreement"/> from the verdicts of a double-reviewed subsample. A
/// (record, candidate) pair judged by exactly two reviewers is compared; one judged by a single
/// reviewer is not part of the subsample and is skipped.
/// </summary>
public static class ClericalReviewAgreement
{
    /// <summary>The number of outcomes a verdict can take — the category count both coefficients are defined over.</summary>
    public static readonly int OutcomeCount = Enum.GetValues<ReviewOutcome>().Length;

    /// <summary>
    /// Within each compared pair the reviewer with the ordinally smaller id is taken as the first
    /// rater and the other as the second. Cohen's κ needs that assignment (its chance term is the
    /// product of the two raters' marginals); AC1 and observed agreement do not depend on it.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// When a (record, candidate) pair carries verdicts from more than two reviewers, or two from
    /// the same reviewer, or when no pair was judged by two reviewers at all.
    /// </exception>
    public static ReviewerAgreement Measure(IReadOnlyList<ReviewVerdict> verdicts)
    {
        ArgumentNullException.ThrowIfNull(verdicts);

        // Measured by Gil.Review. Outcomes travel as their enum ordinal over OutcomeCount
        // categories; the first rater is the reviewer first in UTF-8 byte order (Eyu: ordinal UTF-16),
        // and Gil's PrevalenceMax has no field here and is dropped.
        var agreement = GilReview.ReviewAgreement.Compute(
            [.. verdicts.Select(verdict => new GilReview.Verdict(verdict.RecordId, verdict.CandidateId, (int)verdict.Outcome, verdict.ReviewerId))],
            OutcomeCount);

        var specific = new Dictionary<ReviewOutcome, double>();
        foreach (var outcome in Enum.GetValues<ReviewOutcome>())
        {
            specific[outcome] = agreement.Specific[(int)outcome];
        }

        return new ReviewerAgreement(
            checked((int)agreement.ComparedPairs),
            agreement.Observed,
            agreement.Kappa,
            agreement.Ac1,
            specific);
    }
}
