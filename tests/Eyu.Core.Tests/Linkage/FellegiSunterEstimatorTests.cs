using Eyu.Core.Linkage;
using Xunit;

namespace Eyu.Core.Tests.Linkage;

public class FellegiSunterEstimatorTests
{
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
