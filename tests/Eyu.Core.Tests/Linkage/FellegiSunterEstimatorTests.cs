using Eyu.Core.Linkage;
using Xunit;

namespace Eyu.Core.Tests.Linkage;

public class FellegiSunterEstimatorTests
{
    [Fact]
    public void An_iteration_count_below_one_is_refused_rather_than_reported_as_not_converged()
    {
        var vectors = Enumerable.Range(0, FellegiSunterEstimator.MinimumPairsForEmEstimation)
            .Select(_ => (IReadOnlyDictionary<string, FieldAgreementLevel>)new Dictionary<string, FieldAgreementLevel> { ["name"] = FieldAgreementLevel.Agree })
            .ToList();

        Assert.Throws<ArgumentOutOfRangeException>(() => FellegiSunterEstimator.Estimate(vectors, maxIterations: 0));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void A_match_prior_on_the_boundary_cannot_be_represented(double prior)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FieldLinkageParameters(
            new Dictionary<string, double>(), new Dictionary<string, double>(), prior, EstimationStatus.HeuristicDefault));
    }

    private static IReadOnlyDictionary<string, FieldAgreementLevel> Vector(params (string Field, FieldAgreementLevel Level)[] entries) =>
        entries.ToDictionary(e => e.Field, e => e.Level);

    [Fact]
    public void Below_the_minimum_pair_count_returns_the_heuristic_default_without_running_EM()
    {
        var vectors = new[]
        {
            Vector(("name", FieldAgreementLevel.Agree)),
            Vector(("name", FieldAgreementLevel.Disagree)),
        };

        var parameters = FellegiSunterEstimator.Estimate(vectors);

        Assert.Equal(FellegiSunterEstimator.HeuristicDefaultMAgreeProbability, parameters.MAgreeProbability["name"]);
        Assert.Equal(FellegiSunterEstimator.HeuristicDefaultUAgreeProbability, parameters.UAgreeProbability["name"]);
        Assert.Equal(FellegiSunterEstimator.HeuristicDefaultMatchPrior, parameters.MatchPrior);
        Assert.Equal(EstimationStatus.HeuristicDefault, parameters.Status);
    }

    [Fact]
    public void EM_learns_that_a_discriminating_field_agrees_more_under_match_than_non_match_and_reports_convergence()
    {
        // 10 "matchy" pairs (discriminating field mostly agrees, a noisy field is 50/50) and 10
        // "non-matchy" pairs (discriminating field mostly disagrees) -- enough pairs to clear
        // MinimumPairsForEmEstimation and let EM separate the two regimes.
        var vectors = new List<IReadOnlyDictionary<string, FieldAgreementLevel>>();
        for (var i = 0; i < 10; i++)
        {
            vectors.Add(Vector(
                ("discriminating", i % 10 == 0 ? FieldAgreementLevel.Disagree : FieldAgreementLevel.Agree),
                ("noisy", i % 2 == 0 ? FieldAgreementLevel.Agree : FieldAgreementLevel.Disagree)));
        }
        for (var i = 0; i < 10; i++)
        {
            vectors.Add(Vector(
                ("discriminating", i % 10 == 0 ? FieldAgreementLevel.Agree : FieldAgreementLevel.Disagree),
                ("noisy", i % 2 == 0 ? FieldAgreementLevel.Agree : FieldAgreementLevel.Disagree)));
        }

        var parameters = FellegiSunterEstimator.Estimate(vectors, maxIterations: 200);

        Assert.True(parameters.MAgreeProbability["discriminating"] > parameters.UAgreeProbability["discriminating"]);
        Assert.Equal(EstimationStatus.Converged, parameters.Status);
    }

    [Fact]
    public void A_maxIterations_cap_too_low_to_converge_is_reported_as_NotConverged()
    {
        // Same 20-vector batch as the convergence test above, but capped at 1 iteration -- the
        // parameters move measurably away from their 0.9/0.1 starting point on the very first
        // M-step, so delta cannot fall below the default tolerance in a single pass.
        var vectors = new List<IReadOnlyDictionary<string, FieldAgreementLevel>>();
        for (var i = 0; i < 10; i++)
        {
            vectors.Add(Vector(
                ("discriminating", i % 10 == 0 ? FieldAgreementLevel.Disagree : FieldAgreementLevel.Agree),
                ("noisy", i % 2 == 0 ? FieldAgreementLevel.Agree : FieldAgreementLevel.Disagree)));
        }
        for (var i = 0; i < 10; i++)
        {
            vectors.Add(Vector(
                ("discriminating", i % 10 == 0 ? FieldAgreementLevel.Agree : FieldAgreementLevel.Disagree),
                ("noisy", i % 2 == 0 ? FieldAgreementLevel.Agree : FieldAgreementLevel.Disagree)));
        }

        var parameters = FellegiSunterEstimator.Estimate(vectors, maxIterations: 1);

        Assert.Equal(EstimationStatus.NotConverged, parameters.Status);
    }

    [Fact]
    public void A_model_whose_match_component_agrees_less_is_relabeled_not_returned_as_is()
    {
        // EM converged the wrong way round: the component it calls "match" agrees on every field
        // less often than the one it calls "non-match". Left alone, every LLR flips sign and Match
        // means NonMatch. The mixture is symmetric, so swapping is the same optimum, correctly named.
        var swapped = new FieldLinkageParameters(
            new Dictionary<string, double> { ["name"] = 0.2, ["email"] = 0.1 },
            new Dictionary<string, double> { ["name"] = 0.9, ["email"] = 0.8 },
            0.3,
            EstimationStatus.Converged);

        var fixedUp = FellegiSunterEstimator.WithMatchComponentFirst(swapped);

        Assert.Equal(0.9, fixedUp.MAgreeProbability["name"]);
        Assert.Equal(0.2, fixedUp.UAgreeProbability["name"]);
        Assert.Equal(0.7, fixedUp.MatchPrior, precision: 12);
        Assert.Equal(EstimationStatus.Converged, fixedUp.Status);
        Assert.True(fixedUp.LabelsSwapped);
        Assert.True(FellegiSunterEstimator.ComputeLogLikelihoodRatio(
            Vector(("name", FieldAgreementLevel.Agree), ("email", FieldAgreementLevel.Agree)), fixedUp) > 0);
    }

    [Fact]
    public void A_correctly_labeled_model_is_returned_unchanged()
    {
        var parameters = new FieldLinkageParameters(
            new Dictionary<string, double> { ["name"] = 0.9 },
            new Dictionary<string, double> { ["name"] = 0.1 },
            0.4,
            EstimationStatus.Converged);

        var result = FellegiSunterEstimator.WithMatchComponentFirst(parameters);

        Assert.Same(parameters, result);
        Assert.False(result.LabelsSwapped);
    }

    [Fact]
    public void Relabeling_is_decided_on_the_whole_model_not_on_one_inverted_field()
    {
        // One strongly discriminating field the right way round, one weak field the wrong way
        // round: the model is correctly labeled and the weak field keeps its (negative) weight.
        var parameters = new FieldLinkageParameters(
            new Dictionary<string, double> { ["strong"] = 0.95, ["weak"] = 0.4 },
            new Dictionary<string, double> { ["strong"] = 0.05, ["weak"] = 0.5 },
            0.5,
            EstimationStatus.Converged);

        var result = FellegiSunterEstimator.WithMatchComponentFirst(parameters);

        Assert.Same(parameters, result);
        Assert.True(result.MAgreeProbability["weak"] < result.UAgreeProbability["weak"]);
    }

    [Fact]
    public void Estimate_never_returns_a_model_in_which_agreement_is_evidence_against_a_match()
    {
        // Batches shaped to pull EM in different directions: mostly-agreeing, mostly-disagreeing,
        // balanced, and a two-field batch whose fields disagree with each other. Whatever regime EM
        // lands in, the returned model must read agreement as evidence for a match -- the invariant
        // every downstream LLR sign depends on.
        var batches = new List<List<IReadOnlyDictionary<string, FieldAgreementLevel>>>();
        foreach (var agreeShare in new[] { 0.1, 0.3, 0.5, 0.7, 0.9 })
        {
            var batch = new List<IReadOnlyDictionary<string, FieldAgreementLevel>>();
            for (var i = 0; i < 40; i++)
            {
                var agrees = (i % 10) / 10.0 < agreeShare;
                batch.Add(Vector(
                    ("a", agrees ? FieldAgreementLevel.Agree : FieldAgreementLevel.Disagree),
                    ("b", agrees ^ (i % 3 == 0) ? FieldAgreementLevel.Agree : FieldAgreementLevel.Disagree)));
            }
            batches.Add(batch);
        }

        foreach (var batch in batches)
        {
            var parameters = FellegiSunterEstimator.Estimate(batch, maxIterations: 500);
            var allAgree = Vector(("a", FieldAgreementLevel.Agree), ("b", FieldAgreementLevel.Agree));

            Assert.NotEqual(EstimationStatus.HeuristicDefault, parameters.Status);
            Assert.True(FellegiSunterEstimator.ComputeLogLikelihoodRatio(allAgree, parameters) >= 0.0,
                $"agreement must never count against a match (LabelsSwapped={parameters.LabelsSwapped})");
        }
    }

    [Fact]
    public void ComputeLogLikelihoodRatio_is_positive_when_every_field_agrees()
    {
        var parameters = new FieldLinkageParameters(
            MAgreeProbability: new Dictionary<string, double> { ["name"] = 0.9, ["city"] = 0.9 },
            UAgreeProbability: new Dictionary<string, double> { ["name"] = 0.1, ["city"] = 0.1 },
            MatchPrior: 0.5,
            Status: EstimationStatus.Converged);
        var vector = Vector(("name", FieldAgreementLevel.Agree), ("city", FieldAgreementLevel.Agree));

        var llr = FellegiSunterEstimator.ComputeLogLikelihoodRatio(vector, parameters);

        Assert.True(llr > 0);
    }

    [Fact]
    public void ComputeLogLikelihoodRatio_is_negative_when_every_field_disagrees()
    {
        var parameters = new FieldLinkageParameters(
            MAgreeProbability: new Dictionary<string, double> { ["name"] = 0.9, ["city"] = 0.9 },
            UAgreeProbability: new Dictionary<string, double> { ["name"] = 0.1, ["city"] = 0.1 },
            MatchPrior: 0.5,
            Status: EstimationStatus.Converged);
        var vector = Vector(("name", FieldAgreementLevel.Disagree), ("city", FieldAgreementLevel.Disagree));

        var llr = FellegiSunterEstimator.ComputeLogLikelihoodRatio(vector, parameters);

        Assert.True(llr < 0);
    }

    [Fact]
    public void ComputeLogLikelihoodRatio_ignores_a_field_unseen_during_estimation()
    {
        var parameters = new FieldLinkageParameters(
            MAgreeProbability: new Dictionary<string, double> { ["name"] = 0.9 },
            UAgreeProbability: new Dictionary<string, double> { ["name"] = 0.1 },
            MatchPrior: 0.5,
            Status: EstimationStatus.Converged);
        var vector = Vector(("name", FieldAgreementLevel.Agree), ("unseen_field", FieldAgreementLevel.Agree));

        var llr = FellegiSunterEstimator.ComputeLogLikelihoodRatio(vector, parameters);

        Assert.Equal(Math.Log(0.9 / 0.1), llr, precision: 6);
    }
}
