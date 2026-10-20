using Eyu.Core.Grounding;
using Eyu.Core.Primitives;
using Eyu.Core.Proposals;
using Eyu.Core.Records;
using Xunit;

namespace Eyu.Core.Tests.Quality;

public class KnownTypeVocabularyTests
{
    private static readonly RawRecord[] Erp =
    [
        new("erp-part-1", new Dictionary<string, string?> { ["part_no"] = "6205" }),
        new("erp-part-2", new Dictionary<string, string?> { ["part_no"] = "B-1200" }),
        new("erp-part-3", new Dictionary<string, string?> { ["part_no"] = "S-1" }),
        new("erp-po-1", new Dictionary<string, string?> { ["po_no"] = "PO-1", ["item"] = "6205" }),
    ];

    private static EntityProposal Entity(string id, string name, string type, params string[] denotedBy) =>
        EntityProposal.Create(id, name, type, GroundedClaim.Create(name, [.. (denotedBy.Length == 0 ? ["erp-po-1"] : denotedBy).Select(r => new SourceRef(r))]),
            VocabularyOrigin.Acquired, 0.8, denotedBy: denotedBy);

    [Fact]
    public void A_type_is_named_once_with_its_denoting_records_as_specimens_up_to_the_bound()
    {
        var proposal = new OntologyProposal(
            [Entity("e1", "6205", "part", "erp-part-1"), Entity("e2", "B-1200", "Part", "erp-part-2"), Entity("e3", "S-1", "part", "erp-part-3"), Entity("e4", "PO-1", "purchase_order", "erp-po-1")],
            [], []);

        var vocabulary = KnownTypeVocabulary.FromProposals([(proposal, Erp)]);

        Assert.Equal(["part", "purchase_order"], vocabulary.Select(t => t.Name));
        Assert.Equal(["erp-part-1", "erp-part-2"], vocabulary[0].Specimens.Select(r => r.Id));
    }

    [Fact]
    public void A_type_no_record_denotes_is_handed_by_name_alone()
    {
        // The part is only named by the purchase order: the order is not a record of a part.
        var proposal = new OntologyProposal([Entity("e1", "6205", "part")], [], []);

        var part = Assert.Single(KnownTypeVocabulary.FromProposals([(proposal, Erp)]));

        Assert.Empty(part.Specimens);
    }

    [Fact]
    public void Extending_keeps_the_earlier_spelling_and_adds_only_new_names()
    {
        IReadOnlyList<KnownType> seed = [new KnownType("work_order"), new KnownType("part")];

        var extended = KnownTypeVocabulary.Extend(seed, [new KnownType("WorkOrder"), new KnownType("person")]);

        Assert.Equal(["work_order", "part", "person"], extended.Select(t => t.Name));
    }

    [Fact]
    public void A_call_takes_the_vocabulary_without_specimens_that_are_its_own_records()
    {
        IReadOnlyList<KnownType> vocabulary = [new KnownType("part", [Erp[0], Erp[1]])];

        var forCall = KnownTypeVocabulary.For(vocabulary, [Erp[0]]);

        Assert.Equal("part", forCall[0].Name);
        Assert.Equal(["erp-part-2"], forCall[0].Specimens.Select(r => r.Id));
    }

    [Fact]
    public void Names_typed_alike_compare_type_sets_leniently_over_names_both_runs_wrote()
    {
        var forward = KnownTypeVocabulary.TypesByName([new OntologyProposal([Entity("e1", "베어링 6205", "part"), Entity("e2", "A라인", "production_line"), Entity("e3", "WO-1", "work_order")], [], [])]);
        var reversed = KnownTypeVocabulary.TypesByName([new OntologyProposal([Entity("e1", "베어링6205", "item"), Entity("e2", "A 라인", "ProductionLine")], [], [])]);

        Assert.Equal((1, 2), KnownTypeVocabulary.TypedAlike(forward, reversed));
    }
}
