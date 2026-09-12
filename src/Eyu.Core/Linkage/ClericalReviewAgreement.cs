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

        var byPair = new Dictionary<(string Record, string Candidate), List<ReviewVerdict>>();
        foreach (var verdict in verdicts)
        {
            var key = (verdict.RecordId, verdict.CandidateId);
            if (!byPair.TryGetValue(key, out var list))
            {
                byPair[key] = list = [];
            }

            list.Add(verdict);
        }

        var k = OutcomeCount;
        var table = new int[k, k];   // [first rater's outcome, second rater's outcome]
        var compared = 0;
        foreach (var ((record, candidate), pairVerdicts) in byPair)
        {
            if (pairVerdicts.Count < 2)
            {
                continue;
            }

            if (pairVerdicts.Count > 2)
            {
                throw new ArgumentException(
                    $"Candidate '{candidate}' for record '{record}' was judged {pairVerdicts.Count} times; the protocol double-reviews, so at most two verdicts per pair are expected.",
                    nameof(verdicts));
            }

            var first = pairVerdicts[0];
            var second = pairVerdicts[1];
            var order = string.CompareOrdinal(first.ReviewerId, second.ReviewerId);
            if (order == 0)
            {
                throw new ArgumentException(
                    $"Candidate '{candidate}' for record '{record}' carries two verdicts from reviewer '{first.ReviewerId}'.",
                    nameof(verdicts));
            }

            if (order > 0)
            {
                (first, second) = (second, first);
            }

            table[(int)first.Outcome, (int)second.Outcome]++;
            compared++;
        }

        if (compared == 0)
        {
            throw new ArgumentException("No (record, candidate) pair was judged by two reviewers, so there is no agreement to measure.", nameof(verdicts));
        }

        double agreed = 0;
        var firstMarginal = new double[k];
        var secondMarginal = new double[k];
        for (var i = 0; i < k; i++)
        {
            agreed += table[i, i];
            for (var j = 0; j < k; j++)
            {
                firstMarginal[i] += table[i, j];
                secondMarginal[j] += table[i, j];
            }
        }

        var observed = agreed / compared;

        // Cohen (1960): chance agreement is the product of the two raters' marginal proportions,
        // summed over outcomes. Undefined when both raters used one outcome only (p_e = 1).
        double kappaChance = 0;
        for (var i = 0; i < k; i++)
        {
            kappaChance += (firstMarginal[i] / compared) * (secondMarginal[i] / compared);
        }

        var kappa = kappaChance >= 1.0 ? double.NaN : (observed - kappaChance) / (1.0 - kappaChance);

        // Gwet (2008): chance agreement is 1/(K-1) · Σ_k π_k (1 - π_k), with π_k the two raters'
        // average marginal proportion for outcome k -- it cannot reach 1, so AC1 is always defined.
        double ac1Chance = 0;
        for (var i = 0; i < k; i++)
        {
            var pi = (firstMarginal[i] + secondMarginal[i]) / (2.0 * compared);
            ac1Chance += pi * (1.0 - pi);
        }

        ac1Chance /= k - 1;
        var ac1 = (observed - ac1Chance) / (1.0 - ac1Chance);

        // Specific agreement for outcome k: of all the times either rater said k, how often both did.
        var specific = new Dictionary<ReviewOutcome, double>();
        foreach (var outcome in Enum.GetValues<ReviewOutcome>())
        {
            var i = (int)outcome;
            var denominator = firstMarginal[i] + secondMarginal[i];
            specific[outcome] = denominator == 0 ? double.NaN : 2.0 * table[i, i] / denominator;
        }

        return new ReviewerAgreement(compared, observed, kappa, ac1, specific);
    }
}
