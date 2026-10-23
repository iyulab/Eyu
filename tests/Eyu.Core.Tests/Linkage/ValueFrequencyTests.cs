using Eyu.Core.Linkage;
using Eyu.Core.Records;
using Xunit;

namespace Eyu.Core.Tests.Linkage;

/// <summary>
/// Value frequencies: agreeing on a value most of the population shares counts for less than agreeing
/// on one only a few hold, which one <c>u</c> per field cannot tell apart. Measured once on the
/// population, carried on the supplied parameters, and applied only to exact agreement on a listed value.
/// </summary>
public class ValueFrequencyTests
{
    private static RawRecord Person(string id, string surname, string given, string town) =>
        new(id, new Dictionary<string, string?> { ["surname"] = surname, ["given"] = given, ["town"] = town });

    // Half the population is a Kim; Yeon is one record in ten.
    private static readonly RawRecord[] Population =
    [
        Person("p0", "Kim", "Minji", "Seoul"), Person("p1", "Kim", "Junho", "Busan"),
        Person("p2", "Kim", "Seoyeon", "Daegu"), Person("p3", "Kim", "Jiwoo", "Incheon"),
        Person("p4", "Kim", "Hayun", "Gwangju"), Person("p5", "Lee", "Minjun", "Daejeon"),
        Person("p6", "Park", "Yuna", "Ulsan"), Person("p7", "Choi", "Doyun", "Suwon"),
        Person("p8", "Jung", "Seojun", "Jeju"), Person("p9", "Yeon", "Areum", "Pohang"),
    ];

    private static FieldLinkageParameters Parameters(bool withFrequencies)
    {
        var parameters = FieldLinkageParameters.FromNonMatchAgreement(
            LinkagePipeline.EstimateNonMatchAgreement(Population), matchAgreement: 0.92, matchPrior: 0.01);
        return withFrequencies
            ? parameters with { ValueFrequencies = LinkagePipeline.MeasureValueFrequencies(Population, ["surname", "given"]) }
            : parameters;
    }

    private static double Llr(RawRecord incoming, RawRecord known, FieldLinkageParameters parameters) =>
        LinkagePipeline.Analyze([incoming], [new KnownEntity("k", "known", "Person", [known])], parameters,
                new LinkageOptions(NonMatchThreshold: -100, MatchThreshold: 100))
            .KnownCandidates.Single().LogLikelihoodRatio;

    [Fact]
    public void Shares_are_taken_over_the_records_holding_the_field_in_compared_form()
    {
        RawRecord[] population =
        [
            Person("a", "Kim", "x", "t"), Person("b", " KIM ", "y", "t"), Person("c", "Lee", "z", "t"),
            new("d", new Dictionary<string, string?> { ["surname"] = "  ", ["given"] = "w" }),
        ];

        var frequencies = LinkagePipeline.MeasureValueFrequencies(population, ["surname", "missing"]);

        Assert.Equal(["surname"], frequencies.Keys);
        Assert.Equal(2.0 / 3, frequencies["surname"]["kim"], precision: 12);
        Assert.Equal(1.0 / 3, frequencies["surname"]["Lee"], precision: 12);
    }

    [Fact]
    public void Agreeing_on_a_common_value_counts_for_less_than_on_a_rare_one()
    {
        var parameters = Parameters(withFrequencies: true);

        var commonAgreement = Llr(Person("n", "Kim", "Other", "Nowhere"), Population[0], parameters);
        var rareAgreement = Llr(Person("n", "Yeon", "Other", "Nowhere"), Population[9], parameters);

        Assert.True(rareAgreement > commonAgreement,
            $"agreeing on a surname half the population holds ({commonAgreement:0.00}) must count for less than on one a tenth holds ({rareAgreement:0.00})");
        Assert.Equal(Math.Log(0.5 / 0.1), rareAgreement - commonAgreement, precision: 9);
    }

    [Fact]
    public void Without_frequencies_both_agreements_weigh_the_same()
    {
        var parameters = Parameters(withFrequencies: false);

        Assert.Equal(
            Llr(Person("n", "Kim", "Other", "Nowhere"), Population[0], parameters),
            Llr(Person("n", "Yeon", "Other", "Nowhere"), Population[9], parameters));
    }

    [Fact]
    public void A_value_the_population_does_not_hold_is_scored_as_without_frequencies()
    {
        var known = Person("k", "Ryu", "Somin", "Seoul");
        var incoming = Person("n", "Ryu", "Other", "Nowhere");

        Assert.Equal(Llr(incoming, known, Parameters(withFrequencies: false)), Llr(incoming, known, Parameters(withFrequencies: true)));
    }

    [Fact]
    public void A_field_without_frequencies_and_a_disagreement_are_untouched()
    {
        // town has no frequencies; the surname disagrees, so nothing is adjusted.
        var known = Population[0];
        var incoming = Person("n", "Lee", "Other", "Seoul");

        Assert.Equal(Llr(incoming, known, Parameters(withFrequencies: false)), Llr(incoming, known, Parameters(withFrequencies: true)));
    }

    [Fact]
    public void A_stranger_sharing_only_common_values_is_no_longer_a_match()
    {
        // The same surname and town as a known Kim and nothing else: one u per field reads two agreements;
        // the frequencies say both are what most of the population shares.
        RawRecord[] population = [.. Population, .. Enumerable.Range(0, 30).Select(i => Person($"q{i}", "Kim", $"G{i}", "Seoul"))];
        var known = Person("k", "Kim", "Minji", "Seoul");
        RawRecord stranger = new("n", new Dictionary<string, string?> { ["surname"] = "Kim", ["town"] = "Seoul" });
        var parameters = FieldLinkageParameters.FromNonMatchAgreement(
            LinkagePipeline.EstimateNonMatchAgreement(population), matchAgreement: 0.92, matchPrior: 0.01);
        var withFrequencies = parameters with { ValueFrequencies = LinkagePipeline.MeasureValueFrequencies(population, ["surname", "town"]) };

        var before = Llr(stranger, known, parameters);
        var after = Llr(stranger, known, withFrequencies);

        Assert.True(after < before, $"common-value agreement must weigh less: {before:0.00} → {after:0.00}");
    }
}
