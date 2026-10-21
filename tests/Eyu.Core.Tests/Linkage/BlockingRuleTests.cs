using Eyu.Core.Linkage;
using Eyu.Core.Records;
using Xunit;

namespace Eyu.Core.Tests.Linkage;

/// <summary>
/// Blocking rules: which pairs are compared at all. A pair is compared when, for at least one rule,
/// every field the rule names is present on both records and equal; rules are alternatives.
/// </summary>
public class BlockingRuleTests
{
    private static RawRecord Person(string id, string name, string? birth, string school) =>
        new(id, new Dictionary<string, string?> { ["name"] = name, ["birth"] = birth, ["school"] = school });

    private static readonly RawRecord[] Batch =
    [
        Person("a", "Kim Minjun", "2012-03", "North"),
        Person("b", "Kim Minjun", "2012-03", "South"),
        Person("c", "Kim Minjung", "2012-03", "North"),
        Person("d", "Lee Seoyeon", "2013-07", "North"),
        Person("e", "Park Jiwoo", null, "North"),
        Person("f", "Park Jiwoo", null, "West"),
    ];

    private static IReadOnlyList<(string, string)> Pairs(LinkageAnalysis analysis) =>
        [.. analysis.PairLinkages.Select(p => (p.RecordIdA, p.RecordIdB))];

    [Fact]
    public void Without_rules_every_pair_is_compared()
    {
        var analysis = LinkagePipeline.Analyze(Batch);

        Assert.Equal(Batch.Length * (Batch.Length - 1) / 2, analysis.PairLinkages.Count);
    }

    [Fact]
    public void A_rule_compares_only_records_that_share_its_fields()
    {
        var analysis = LinkagePipeline.Analyze(Batch, new LinkageOptions(BlockingRules: [["name"]]));

        Assert.Equal([("a", "b"), ("e", "f")], Pairs(analysis));
    }

    [Fact]
    public void Rules_are_alternatives_so_a_misspelt_name_is_caught_by_another_rule()
    {
        var analysis = LinkagePipeline.Analyze(Batch, new LinkageOptions(BlockingRules: [["name"], ["birth", "school"]]));

        // a–c differ in name but share birth and school; a–b share the name. In the unblocked order.
        Assert.Equal([("a", "b"), ("a", "c"), ("e", "f")], Pairs(analysis));
    }

    [Fact]
    public void A_blank_field_never_puts_two_records_in_one_block()
    {
        var analysis = LinkagePipeline.Analyze(Batch, new LinkageOptions(BlockingRules: [["birth", "school"]]));

        // e and f have no birth, so neither is in any block of this rule — not one block of the blank.
        Assert.Equal([("a", "c")], Pairs(analysis));
    }

    [Fact]
    public void Matching_against_known_entities_is_blocked_by_the_same_rules()
    {
        var known = new KnownEntity("key:minjun", "Kim Minjun", "Person", [Person("old-1", "Kim Minjun", "2012-03", "North")]);
        var newcomers = new[] { Person("n1", "Kim Minjun", "2012-03", "North"), Person("n2", "Choi Yuna", "2012-03", "East") };
        var parameters = FieldLinkageParameters.FromNonMatchAgreement(
            BinaryLevels.Of(new Dictionary<string, double> { ["name"] = 0.01, ["birth"] = 0.02, ["school"] = 0.25 }), matchAgreement: 0.95, matchPrior: 0.01);

        var analysis = LinkagePipeline.Analyze(newcomers, [known], parameters, new LinkageOptions(BlockingRules: [["name"]]));

        var candidate = Assert.Single(analysis.KnownCandidates);
        Assert.Equal(("n1", "key:minjun", LinkageClassification.Match), (candidate.RecordId, candidate.KnownEntityKey, candidate.Classification));
        Assert.Empty(analysis.PairLinkages);
    }

    [Fact]
    public void Values_in_one_block_compare_as_the_field_comparator_compares_them_exactly()
    {
        var batch = new[] { Person("x", " kim minjun ", "2012-03", "North"), Person("y", "KIM MINJUN", "2012-03", "South") };

        var analysis = LinkagePipeline.Analyze(batch, new LinkageOptions(BlockingRules: [["name"]]));

        Assert.Equal([("x", "y")], Pairs(analysis));
    }

    [Fact]
    public void A_rule_naming_no_field_is_refused()
    {
        Assert.Throws<ArgumentException>(() => LinkagePipeline.Analyze(Batch, new LinkageOptions(BlockingRules: [[]])));
        Assert.Throws<ArgumentException>(() => LinkagePipeline.Analyze(Batch, new LinkageOptions(BlockingRules: [["name", " "]])));
    }
}
