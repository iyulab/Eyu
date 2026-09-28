using Eyu.Core.Linkage;
using Eyu.Core.Records;
using Xunit;

namespace Eyu.Core.Tests.Linkage;

/// <summary>
/// Record linkage across calls: a call's records compared with the records of entities earlier calls
/// identified. The evidence is the same Fellegi-Sunter evidence the call's own pairs get, in one
/// parameter estimate; known records are never compared with each other, and they never join the call's
/// clusters — they are not the call's records.
/// </summary>
public class KnownEntityLinkageTests
{
    private static RawRecord Equipment(string id, string code, string name, string plant, string maker) =>
        new(id, new Dictionary<string, string?> { ["asset_code"] = code, ["name"] = name, ["plant"] = plant, ["maker"] = maker });

    private static readonly KnownEntity Press = new("key:press", "Press 4", "Equipment", [Equipment("old-1", "PRS-004", "Press 4", "Ansan", "Hanwha")]);
    private static readonly KnownEntity Conveyor = new("key:conveyor", "Conveyor 12", "Equipment", [Equipment("old-2", "CNV-012", "Conveyor 12", "Ansan", "Daifuku")]);
    private static readonly KnownEntity Compressor = new("key:compressor", "Compressor 2", "Equipment", [Equipment("old-3", "CMP-002", "Compressor 2", "Siheung", "Atlas")]);

    private static readonly RawRecord[] NewExport =
    [
        Equipment("new-1", "PRS-004", "Press 4", "Ansan", "Hanwha"),
        Equipment("new-2", "LFT-031", "Lift 31", "Pyeongtaek", "Otis"),
        Equipment("new-3", "WLD-007", "Welder 7", "Siheung", "Lincoln"),
    ];

    [Fact]
    public void A_record_agreeing_with_a_known_entitys_record_is_a_match_to_that_key_only()
    {
        var analysis = LinkagePipeline.Analyze(NewExport, [Press, Conveyor, Compressor]);

        var match = Assert.Single(analysis.KnownCandidates, c => c.Classification == LinkageClassification.Match);
        Assert.Equal(("new-1", "key:press"), (match.RecordId, match.KnownEntityKey));
        Assert.DoesNotContain(analysis.KnownCandidates, c => c.RecordId is "new-2" or "new-3" && c.Classification == LinkageClassification.Match);
    }

    [Fact]
    public void Known_records_stay_out_of_the_calls_clusters_and_pairs()
    {
        var analysis = LinkagePipeline.Analyze(NewExport, [Press, Conveyor, Compressor]);

        var clustered = analysis.Clustering.Clusters.SelectMany(c => c.RecordIds).ToList();
        Assert.Equal(["new-1", "new-2", "new-3"], clustered.Order(StringComparer.Ordinal));
        Assert.All(analysis.PairLinkages, p => Assert.StartsWith("new-", p.RecordIdA, StringComparison.Ordinal));
        Assert.All(analysis.PairLinkages, p => Assert.StartsWith("new-", p.RecordIdB, StringComparison.Ordinal));
    }

    [Fact]
    public void Known_entities_are_not_compared_with_each_other()
    {
        // Two known entities written alike: had they been compared, a caller's own decision that they
        // are two entities would be second-guessed. With no record of the call's to compare, there is
        // nothing to estimate.
        var twin = Press with { Key = "key:press-twin", DenotingRecords = [Equipment("old-9", "PRS-004", "Press 4", "Ansan", "Hanwha")] };

        var analysis = LinkagePipeline.Analyze([], [Press, twin]);

        Assert.Empty(analysis.KnownCandidates);
        Assert.Null(analysis.Parameters);
    }

    [Fact]
    public void One_record_is_enough_when_there_are_known_entities_to_compare_it_with()
    {
        var analysis = LinkagePipeline.Analyze([NewExport[0]], [Press, Conveyor, Compressor]);

        Assert.NotNull(analysis.Parameters);
        Assert.Contains(analysis.KnownCandidates, c => c.RecordId == "new-1" && c.KnownEntityKey == "key:press");
    }

    [Fact]
    public void Where_records_do_not_denote_entities_nothing_is_linked_to_a_known_one()
    {
        var analysis = LinkagePipeline.Analyze(NewExport, [Press], new LinkageOptions(RecordsDenoteEntities: false));

        Assert.Empty(analysis.KnownCandidates);
    }

    [Fact]
    public void Without_known_entities_the_analysis_is_the_one_a_call_always_got()
    {
        var before = LinkagePipeline.Analyze(NewExport);
        var withNone = LinkagePipeline.Analyze(NewExport, []);

        Assert.Equal(before.PairLinkages, withNone.PairLinkages);
        Assert.Equal(before.Parameters!.MatchPrior, withNone.Parameters!.MatchPrior);
        Assert.Equal(before.Parameters.MAgreeProbability, withNone.Parameters.MAgreeProbability);
        Assert.Equal(before.Parameters.UAgreeProbability, withNone.Parameters.UAgreeProbability);
        Assert.Empty(withNone.KnownCandidates);
    }

    [Fact]
    public void A_decided_known_match_takes_the_linkage_posterior_and_other_entities_keep_their_confidence()
    {
        var analysis = LinkagePipeline.Analyze(NewExport, [Press, Conveyor, Compressor]);

        var matched = LinkageConfidenceAdjuster.AdjustForKnownEntity(["new-1"], "key:press", 0.3, analysis);
        var unsupported = LinkageConfidenceAdjuster.AdjustForKnownEntity(["new-2"], "key:press", 0.3, analysis);

        Assert.True(matched > 0.9, $"a pre-filter match should dominate a hesitant model; got {matched}");
        Assert.Equal(0.3, unsupported);
    }
}
