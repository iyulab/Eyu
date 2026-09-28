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
}
