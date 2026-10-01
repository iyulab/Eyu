using Eyu.Core.Grounding;
using Eyu.Core.Primitives;
using Eyu.Core.Proposals;
using Eyu.Core.Records;
using Xunit;

namespace Eyu.Core.Tests.Quality;

public class KnownEntityChainTests
{
    private static readonly RawRecord[] Records =
    [
        new("erp-eq-1", new Dictionary<string, string?> { ["asset_code"] = "PRS-004" }),
        new("erp-eq-2", new Dictionary<string, string?> { ["asset_code"] = "CNV-012" }),
        new("erp-po-1", new Dictionary<string, string?> { ["for_asset"] = "PRS-004" }),
    ];

    private static EntityProposal Entity(string id, string name, string type, params string[] denotedBy) =>
        EntityProposal.Create(id, name, type, GroundedClaim.Create($"{name}", [.. (denotedBy.Length == 0 ? ["erp-po-1"] : denotedBy).Select(r => new SourceRef(r))]),
            VocabularyOrigin.Acquired, 0.8, denotedBy: denotedBy);

    [Fact]
    public void Denoted_entities_become_known_under_the_callers_key_with_their_records()
    {
        var proposal = new OntologyProposal(
            [Entity("e1", "Press 4", "Equipment", "erp-eq-1"), Entity("e2", "Conveyor 12", "Equipment", "erp-eq-2"), Entity("e3", "Press 4", "Machine")],
            [], []);

        var known = KnownEntityChain.FromProposal(proposal, Records, e => $"iri:{e.EntityId}");

        Assert.Equal(["iri:e1", "iri:e2"], known.Select(k => k.Key));
        Assert.Equal("erp-eq-1", Assert.Single(known[0].DenotingRecords).Id);
        Assert.Equal("PRS-004", known[0].DenotingRecords[0].Fields["asset_code"]);
    }

    [Fact]
    public void Entities_one_key_names_are_one_known_entity_and_a_null_key_leaves_one_out()
    {
        var proposal = new OntologyProposal(
            [Entity("e1", "Press 4", "Equipment", "erp-eq-1"), Entity("e2", "Press 04", "Equipment", "erp-po-1"), Entity("e3", "Conveyor 12", "Equipment", "erp-eq-2")],
            [], []);

        var known = KnownEntityChain.FromProposal(proposal, Records, e => e.EntityId == "e3" ? null : "iri:press");

        var press = Assert.Single(known);
        Assert.Equal(["erp-eq-1", "erp-po-1"], press.DenotingRecords.Select(r => r.Id).Order(StringComparer.Ordinal));
    }

    private static readonly CrossSourceCase Plant = new("plant", [],
    [
        new SameThing("press 4", ["Press 4", "PRS-004"], "erp-eq-1"),
        new SameThing("conveyor 12", ["Conveyor 12", "CNV-012"], "erp-eq-2"),
    ]);

    [Fact]
    public void A_step_names_the_things_it_only_mentioned_and_the_ones_it_made_known()
    {
        // A maintenance log first: it denotes the conveyor's own record but only mentions the press.
        var proposal = new OntologyProposal(
            [Entity("e1", "Conveyor 12", "Equipment", "erp-eq-2"), Entity("e2", "Press 4", "Equipment")],
            [], []);

        var step = KnownEntityChain.Step("cmms", proposal, Records, e => $"iri:{e.EntityId}", [], Plant);

        Assert.Equal(0, step.KnownIn);
        Assert.Equal(0, step.MatchedToKnown);
        Assert.Equal(1, step.NewlyKnown);
        Assert.Equal(["conveyor 12"], step.ThingsMadeKnown);
        Assert.Equal(["press 4"], step.ThingsMentionedOnly);
    }

    [Fact]
    public void A_thing_matched_to_a_known_entity_is_carried_even_when_nothing_denotes_it_here()
    {
        var knownPress = new KnownEntity("iri:press", "Press 4", "Equipment", [Records[0]]);
        var proposal = new OntologyProposal(
            [EntityProposal.Create("e1", "PRS-004", "Equipment", GroundedClaim.Create("PRS-004", [new SourceRef("erp-po-1")]),
                VocabularyOrigin.Acquired, 0.8, denotedBy: [], knownEntityKey: "iri:press")],
            [], []);

        var step = KnownEntityChain.Step("erp", proposal, Records, e => $"iri:{e.EntityId}", [knownPress], Plant);

        Assert.Equal(1, step.KnownIn);
        Assert.Equal(1, step.MatchedToKnown);
        Assert.Equal(0, step.NewlyKnown);
        Assert.Empty(step.ThingsMadeKnown);
        Assert.Empty(step.ThingsMentionedOnly);
    }
}
