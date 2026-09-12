using Eyu.Core.Linkage;
using Xunit;

namespace Eyu.Core.Tests.Linkage;

public class ClericalReviewAgreementTests
{
    private static IEnumerable<ReviewVerdict> Pair(int index, ReviewOutcome first, ReviewOutcome second)
    {
        yield return new ReviewVerdict($"r{index}", "c", first, "alice");
        yield return new ReviewVerdict($"r{index}", "c", second, "bob");
    }

    [Fact]
    public void Perfect_agreement_scores_one_on_every_coefficient()
    {
        var verdicts = Enumerable.Range(0, 6)
            .SelectMany(i => Pair(i, i % 2 == 0 ? ReviewOutcome.Same : ReviewOutcome.Different, i % 2 == 0 ? ReviewOutcome.Same : ReviewOutcome.Different))
            .ToList();

        var agreement = ClericalReviewAgreement.Measure(verdicts);

        Assert.Equal(6, agreement.ComparedPairs);
        Assert.Equal(1.0, agreement.ObservedAgreement, 12);
        Assert.Equal(1.0, agreement.CohenKappa, 12);
        Assert.Equal(1.0, agreement.GwetAc1, 12);
        Assert.Equal(1.0, agreement.SpecificAgreement[ReviewOutcome.Same], 12);
        Assert.Equal(1.0, agreement.SpecificAgreement[ReviewOutcome.Different], 12);
        Assert.True(double.IsNaN(agreement.SpecificAgreement[ReviewOutcome.CannotTell]));
    }

    // A hand-computed 2x2: 10 pairs, both Same on 5, both Different on 3, alice Same / bob
    // Different on 2. p_o = 0.8; alice's marginals 7/3, bob's 5/5 (out of 10).
    [Fact]
    public void Kappa_and_ac1_match_a_hand_calculation()
    {
        var verdicts = new List<ReviewVerdict>();
        var index = 0;
        for (var i = 0; i < 5; i++) verdicts.AddRange(Pair(index++, ReviewOutcome.Same, ReviewOutcome.Same));
        for (var i = 0; i < 3; i++) verdicts.AddRange(Pair(index++, ReviewOutcome.Different, ReviewOutcome.Different));
        for (var i = 0; i < 2; i++) verdicts.AddRange(Pair(index++, ReviewOutcome.Same, ReviewOutcome.Different));

        var agreement = ClericalReviewAgreement.Measure(verdicts);

        // Cohen: p_e = 0.7*0.5 + 0.3*0.5 = 0.5 -> (0.8 - 0.5) / 0.5 = 0.6
        Assert.Equal(0.8, agreement.ObservedAgreement, 12);
        Assert.Equal(0.6, agreement.CohenKappa, 12);
        // Gwet, K = 3: pi_Same = (0.7+0.5)/2 = 0.6, pi_Different = (0.3+0.5)/2 = 0.4, pi_CannotTell = 0
        //   p_e = (0.6*0.4 + 0.4*0.6 + 0) / 2 = 0.24 -> (0.8 - 0.24) / 0.76
        Assert.Equal((0.8 - 0.24) / 0.76, agreement.GwetAc1, 12);
        // Specific: Same 2*5/(7+5) ; Different 2*3/(3+5)
        Assert.Equal(10.0 / 12.0, agreement.SpecificAgreement[ReviewOutcome.Same], 12);
        Assert.Equal(6.0 / 8.0, agreement.SpecificAgreement[ReviewOutcome.Different], 12);
    }

    // The case the protocol's strata produce: nearly every verdict is Same. Observed agreement
    // is high, kappa collapses toward zero under that prevalence, AC1 does not.
    [Fact]
    public void Under_one_sided_prevalence_kappa_collapses_and_ac1_holds()
    {
        var verdicts = new List<ReviewVerdict>();
        var index = 0;
        for (var i = 0; i < 48; i++) verdicts.AddRange(Pair(index++, ReviewOutcome.Same, ReviewOutcome.Same));
        verdicts.AddRange(Pair(index++, ReviewOutcome.Same, ReviewOutcome.Different));
        verdicts.AddRange(Pair(index++, ReviewOutcome.Different, ReviewOutcome.Same));

        var agreement = ClericalReviewAgreement.Measure(verdicts);

        Assert.Equal(0.96, agreement.ObservedAgreement, 12);
        Assert.True(agreement.CohenKappa < 0.0, $"kappa was {agreement.CohenKappa}");
        Assert.True(agreement.GwetAc1 > 0.9, $"AC1 was {agreement.GwetAc1}");
    }

    [Fact]
    public void Kappa_is_undefined_when_both_reviewers_used_one_outcome_and_ac1_is_not()
    {
        var verdicts = Enumerable.Range(0, 4).SelectMany(i => Pair(i, ReviewOutcome.Same, ReviewOutcome.Same)).ToList();

        var agreement = ClericalReviewAgreement.Measure(verdicts);

        Assert.True(double.IsNaN(agreement.CohenKappa));
        Assert.Equal(1.0, agreement.GwetAc1, 12);
    }

    [Fact]
    public void Single_reviewed_pairs_are_not_compared_and_a_third_reviewer_is_refused()
    {
        var verdicts = new List<ReviewVerdict>
        {
            new("r0", "c", ReviewOutcome.Same, "alice"),          // single-reviewed: skipped
            new("r1", "c", ReviewOutcome.Same, "alice"),
            new("r1", "c", ReviewOutcome.Same, "bob"),
        };

        Assert.Equal(1, ClericalReviewAgreement.Measure(verdicts).ComparedPairs);

        verdicts.Add(new ReviewVerdict("r1", "c", ReviewOutcome.Same, "carol"));
        Assert.Throws<ArgumentException>(() => ClericalReviewAgreement.Measure(verdicts));
    }

    [Fact]
    public void Nothing_double_reviewed_is_refused_rather_than_scored_as_agreement()
    {
        Assert.Throws<ArgumentException>(() => ClericalReviewAgreement.Measure([new ReviewVerdict("r0", "c", ReviewOutcome.Same, "alice")]));
    }
}
