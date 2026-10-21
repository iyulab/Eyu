using Eyu.Core.Linkage;
using Eyu.Core.Records;
using Xunit;

namespace Eyu.Core.Tests.Linkage;

public class MultiLevelAgreementTests
{
    private static readonly LinkageOptions Levels = LinkageOptions.Default with { Comparison = FieldComparison.JaroWinkler(1.0, 0.92, 0.88) };

    private static RawRecord Record(string id, string name) => new(id, new Dictionary<string, string?> { ["name"] = name });

    private static FieldAgreementLevel LevelOf(string a, string b) =>
        FieldComparator.Compare(Record("a", a), Record("b", b), Levels)["name"];

    [Theory]
    [InlineData("MARTHA", "MARTHA", 3)] // equal: every threshold cleared
    [InlineData("MARTHA", "MARHTA", 2)] // Jaro-Winkler 0.961
    [InlineData("JELLYFISH", "SMELLYFISH", 1)] // 0.896
    [InlineData("DWAYNE", "DUANE", 0)] // 0.840
    public void A_pair_lands_on_the_level_of_the_thresholds_it_clears(string a, string b, int rank)
    {
        Assert.Equal(new FieldAgreementLevel(rank, 4), LevelOf(a, b));
    }

    [Fact]
    public void One_threshold_and_exact_matching_keep_two_levels()
    {
        var jw = FieldComparator.Compare(Record("a", "MARTHA"), Record("b", "MARHTA"), LinkageOptions.Default with { Comparison = FieldComparison.JaroWinkler() })["name"];
        var exact = FieldComparator.Compare(Record("a", "MARTHA"), Record("b", "MARHTA"))["name"];

        Assert.Equal(FieldAgreementLevel.Agree, jw);
        Assert.Equal(FieldAgreementLevel.Disagree, exact);
    }

    [Theory]
    [InlineData(new[] { 0.88, 0.92 })]
    [InlineData(new[] { 0.92, 0.92 })]
    public void Thresholds_that_are_not_strictly_descending_are_refused(double[] thresholds)
    {
        Assert.Throws<ArgumentException>(() => new LinkageOptions(Comparison: new FieldComparison(FieldComparisonMethod.JaroWinkler, thresholds)).Validate());
    }

    [Fact]
    public void A_similarity_method_without_a_threshold_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new LinkageOptions(Comparison: new FieldComparison(FieldComparisonMethod.TokenJaccard, [])).Validate());
    }

    [Fact]
    public void Comparisons_with_the_same_thresholds_are_equal()
    {
        Assert.Equal(FieldComparison.JaroWinkler(1.0, 0.9), FieldComparison.JaroWinkler(1.0, 0.9));
        Assert.Equal(FieldComparison.JaroWinkler(1.0, 0.9).GetHashCode(), FieldComparison.JaroWinkler(1.0, 0.9).GetHashCode());
        Assert.NotEqual(FieldComparison.JaroWinkler(1.0, 0.9), FieldComparison.JaroWinkler(0.9));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 2)]
    [InlineData(-1, 3)]
    public void A_level_outside_its_scale_is_refused(int rank, int count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FieldAgreementLevel(rank, count));
    }

    [Fact]
    public void Em_fits_a_near_miss_between_agreement_and_disagreement()
    {
        static IReadOnlyDictionary<string, FieldAgreementLevel> Pair(int nameRank, int cityRank) => new Dictionary<string, FieldAgreementLevel>
        {
            ["name"] = new(nameRank, 3),
            ["city"] = new(cityRank, 2),
            ["phone"] = new(cityRank, 2),
        };

        // 40 matches: name exact (30) or near (10); 400 non-matches: name near (20) or different (380).
        IReadOnlyList<IReadOnlyDictionary<string, FieldAgreementLevel>> vectors =
        [
            .. Enumerable.Repeat(Pair(2, 1), 30), .. Enumerable.Repeat(Pair(1, 1), 10),
            .. Enumerable.Repeat(Pair(1, 0), 20), .. Enumerable.Repeat(Pair(0, 0), 380),
        ];

        var parameters = FellegiSunterEstimator.Estimate(vectors);
        double Weight(int rank) => Math.Log(parameters.MLevelProbabilities["name"][rank] / parameters.ULevelProbabilities["name"][rank]);

        Assert.Equal(EstimationStatus.Converged, parameters.Status);
        Assert.Equal(3, parameters.MLevelProbabilities["name"].Count);
        Assert.Equal(1.0, parameters.MLevelProbabilities["name"].Sum(), 12);
        Assert.Equal(1.0, parameters.ULevelProbabilities["name"].Sum(), 12);
        Assert.True(Weight(2) > Weight(1), $"exact {Weight(2)} should outweigh near {Weight(1)}");
        Assert.True(Weight(1) > Weight(0), $"near {Weight(1)} should outweigh different {Weight(0)}");
        Assert.True(Weight(1) > 0, $"near names favour a match here ({Weight(1)})");
    }

    [Fact]
    public void A_field_compared_on_two_scales_is_refused()
    {
        IReadOnlyList<IReadOnlyDictionary<string, FieldAgreementLevel>> vectors =
        [
            new Dictionary<string, FieldAgreementLevel> { ["name"] = new(1, 3) },
            new Dictionary<string, FieldAgreementLevel> { ["name"] = FieldAgreementLevel.Agree },
        ];

        Assert.Throws<ArgumentException>(() => FellegiSunterEstimator.Estimate(vectors));
    }

    [Fact]
    public void Scoring_with_parameters_fitted_on_another_scale_is_refused()
    {
        var parameters = new FieldLinkageParameters(
            BinaryLevels.Of(new Dictionary<string, double> { ["name"] = 0.9 }),
            BinaryLevels.Of(new Dictionary<string, double> { ["name"] = 0.1 }),
            0.1,
            EstimationStatus.Supplied);

        Assert.Throws<ArgumentException>(() => FellegiSunterEstimator.ComputeLogLikelihoodRatio(
            new Dictionary<string, FieldAgreementLevel> { ["name"] = new(2, 3) }, parameters));
    }

    [Fact]
    public void Fixed_nonmatch_agreement_on_another_scale_is_refused()
    {
        IReadOnlyList<IReadOnlyDictionary<string, FieldAgreementLevel>> vectors =
            [.. Enumerable.Repeat<IReadOnlyDictionary<string, FieldAgreementLevel>>(new Dictionary<string, FieldAgreementLevel> { ["name"] = new(2, 3) }, 10)];

        Assert.Throws<ArgumentException>(() => FellegiSunterEstimator.Estimate(
            vectors, fixedNonMatchAgreement: BinaryLevels.Of(new Dictionary<string, double> { ["name"] = 0.1 })));
    }

    [Fact]
    public void Nonmatch_agreement_is_measured_per_level()
    {
        string[] names = ["MARTHA", "MARHTA", "JELLYFISH", "SMELLYFISH", "DWAYNE", "DUANE", "DIXON", "KOWALSKI"];
        var population = names.Select((n, i) => Record($"r{i}", n)).ToList();

        var u = LinkagePipeline.EstimateNonMatchAgreement(population, Levels);

        Assert.Equal(4, u["name"].Count);
        Assert.Equal(1.0, u["name"].Sum(), 12);
        Assert.True(u["name"][0] > u["name"][2], "most random pairs of different names disagree");
    }

    [Fact]
    public void Supplied_parameters_put_the_match_agreement_on_the_top_level()
    {
        var parameters = FieldLinkageParameters.FromNonMatchAgreement(
            new Dictionary<string, IReadOnlyList<double>> { ["name"] = [0.97, 0.02, 0.01] }, matchAgreement: 0.9, matchPrior: 0.01);

        Assert.Equal([0.05, 0.05, 0.9], parameters.MLevelProbabilities["name"].Select(p => Math.Round(p, 12)));
        Assert.Equal([0.97, 0.02, 0.01], parameters.ULevelProbabilities["name"]);
    }
}
