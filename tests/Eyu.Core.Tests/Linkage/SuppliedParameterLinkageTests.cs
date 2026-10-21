using Eyu.Core.Linkage;
using Eyu.Core.Records;
using Xunit;

namespace Eyu.Core.Tests.Linkage;

/// <summary>
/// Fitting once and scoring many times: <c>u</c> measured over random pairs of a population, the
/// parameters kept, and each new record scored against known entities without EM running on its own
/// pairs. The case it exists for is one new record against many known ones, where a fit on that call's
/// pairs reads what most pairs share as identity.
/// </summary>
public class SuppliedParameterLinkageTests
{
    private static readonly string[] Schools = ["North", "South", "East", "West"];

    private static RawRecord Pupil(string id, int i, string? phone = null) => new(id, new Dictionary<string, string?>
    {
        ["name"] = $"Pupil {i:000}",
        ["birth"] = $"20{10 + (i % 6)}-{1 + (i % 12):00}",
        ["school"] = Schools[i % Schools.Length],
        ["grade"] = (1 + (i % 6)).ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["phone"] = phone ?? $"{(i * 7919) % 10000:0000}",
    });

    private static readonly IReadOnlyList<RawRecord> Population = [.. Enumerable.Range(0, 240).Select(i => Pupil($"p{i}", i))];

    private static readonly IReadOnlyList<KnownEntity> Known =
        [.. Population.Select(r => new KnownEntity($"key:{r.Id}", r.Fields["name"]!, "Pupil", [r]))];

    [Fact]
    public void Nonmatch_agreement_is_the_share_of_pairs_that_agree_on_each_field()
    {
        var u = LinkagePipeline.EstimateNonMatchAgreement(Population);

        // Four schools evenly spread: of all 240·239/2 pairs, 4·(60·59/2) agree.
        Assert.Equal(4.0 * (60 * 59 / 2) / (240 * 239 / 2), u["school"][^1], 6);
        // Every name is different: no pair agrees, and the value is held at the evidence floor, not 0.
        Assert.Equal(FellegiSunterEstimator.AgreementProbabilityFloor, u["name"][^1]);
    }

    [Fact]
    public void A_population_past_the_pair_budget_is_sampled_deterministically()
    {
        var first = LinkagePipeline.EstimateNonMatchAgreement(Population, maxPairs: 2_000, seed: 3);
        var again = LinkagePipeline.EstimateNonMatchAgreement(Population, maxPairs: 2_000, seed: 3);

        Assert.Equal(first, again);
        Assert.InRange(first["school"][^1], 0.18, 0.32);
    }

    [Fact]
    public void A_field_never_present_on_both_sides_has_no_nonmatch_agreement()
    {
        var withBlankPhones = Population.Select((r, i) => new RawRecord(r.Id, new Dictionary<string, string?>(r.Fields) { ["phone"] = null })).ToList();

        var u = LinkagePipeline.EstimateNonMatchAgreement(withBlankPhones);

        Assert.False(u.ContainsKey("phone"));
    }

    [Fact]
    public void One_new_record_scored_with_supplied_parameters_matches_only_the_entity_it_repeats()
    {
        var parameters = FieldLinkageParameters.FromNonMatchAgreement(LinkagePipeline.EstimateNonMatchAgreement(Population), matchAgreement: 0.95, matchPrior: 0.01);
        var reentered = Pupil("new-1", 7) with { Id = "new-1" };

        var analysis = LinkagePipeline.Analyze([reentered], Known, parameters);

        var match = Assert.Single(analysis.KnownCandidates, c => c.Classification == LinkageClassification.Match);
        Assert.Equal("key:p7", match.KnownEntityKey);
        Assert.Same(parameters, analysis.Parameters);
        Assert.Equal(EstimationStatus.Supplied, analysis.Parameters!.Status);
    }

    [Fact]
    public void A_newcomer_sharing_only_school_and_grade_is_not_matched_with_supplied_parameters()
    {
        var parameters = FieldLinkageParameters.FromNonMatchAgreement(LinkagePipeline.EstimateNonMatchAgreement(Population), matchAgreement: 0.95, matchPrior: 0.01);
        var newcomer = new RawRecord("new-2", new Dictionary<string, string?>
        {
            ["name"] = "Someone Else",
            ["birth"] = "2009-02",
            ["school"] = Schools[1],
            ["grade"] = "2",
            ["phone"] = "0000",
        });

        var analysis = LinkagePipeline.Analyze([newcomer], Known, parameters);

        Assert.DoesNotContain(analysis.KnownCandidates, c => c.Classification == LinkageClassification.Match);
    }

    [Fact]
    public void Em_with_fixed_nonmatch_agreement_moves_only_m_and_the_prior()
    {
        var u = BinaryLevels.Of(new Dictionary<string, double> { ["a"] = 0.05, ["b"] = 0.2, ["c"] = 0.1 });
        var agreeAll = new Dictionary<string, FieldAgreementLevel> { ["a"] = FieldAgreementLevel.Agree, ["b"] = FieldAgreementLevel.Agree, ["c"] = FieldAgreementLevel.Agree };
        var agreeB = new Dictionary<string, FieldAgreementLevel> { ["a"] = FieldAgreementLevel.Disagree, ["b"] = FieldAgreementLevel.Agree, ["c"] = FieldAgreementLevel.Disagree };
        var agreeNone = new Dictionary<string, FieldAgreementLevel> { ["a"] = FieldAgreementLevel.Disagree, ["b"] = FieldAgreementLevel.Disagree, ["c"] = FieldAgreementLevel.Disagree };
        IReadOnlyList<IReadOnlyDictionary<string, FieldAgreementLevel>> vectors =
            [.. Enumerable.Repeat(agreeAll, 20), .. Enumerable.Repeat(agreeB, 30), .. Enumerable.Repeat(agreeNone, 150)];

        var fitted = FellegiSunterEstimator.Estimate(vectors, fixedNonMatchAgreement: u);

        Assert.Equal(u, fitted.ULevelProbabilities);
        Assert.All(fitted.MLevelProbabilities, kv => Assert.True(kv.Value[^1] > u[kv.Key][^1], $"{kv.Key}: m {kv.Value[^1]} should exceed u {u[kv.Key][^1]}"));
        Assert.InRange(fitted.MatchPrior, 0.05, 0.2);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void Match_agreement_must_be_a_probability_strictly_inside_zero_and_one(double matchAgreement)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FieldLinkageParameters.FromNonMatchAgreement(BinaryLevels.Of(new Dictionary<string, double> { ["a"] = 0.1 }), matchAgreement, 0.01));
    }
}
