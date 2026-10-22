using Eyu.Core.Declared;
using Eyu.Core.Judgment;
using Eyu.Core.Linkage;
using Eyu.Core.Primitives;
using Eyu.Core.Records;
using Xunit;

namespace Eyu.Core.Tests.Linkage;

/// <summary>
/// Rows of one table with different keys are different rows. Measured: four work orders, each its own
/// number, two of them sharing a quantity and a note — in a nine-record batch with no true duplicate,
/// the fit read the shared quantity and note as identity and pre-linked the two orders as one entity
/// (log-likelihood ratio 9.5 undeclared, 9.2 with the references declared), and the model was told not
/// to re-decide. A declared key makes such a pair a cannot-link: neither compared nor fitted on.
/// </summary>
public class DistinctByKeyTests
{
    private static readonly SubjectRef Supplier = SubjectRef.Create("supplier");
    private static readonly SubjectRef Item = SubjectRef.Create("item");
    private static readonly SubjectRef WorkOrder = SubjectRef.Create("work_order");

    private static RawRecord Row(string id, SubjectRef subject, params (string Field, string Value)[] fields) =>
        new(id, fields.ToDictionary(f => f.Field, f => (string?)f.Value), subject);

    private static readonly RawRecord[] Records =
    [
        Row("s10", Supplier, ("supplier_id", "S-10"), ("supplier_name", "Daehan Fasteners")),
        Row("s20", Supplier, ("supplier_id", "S-20"), ("supplier_name", "Hanil Bearings")),
        Row("i100", Item, ("item_code", "I-100"), ("item_name", "Hex bolt M8"), ("supplier_id", "S-10")),
        Row("i200", Item, ("item_code", "I-200"), ("item_name", "Hex nut M8"), ("supplier_id", "S-10")),
        Row("i300", Item, ("item_code", "I-300"), ("item_name", "Ball bearing 6204"), ("supplier_id", "S-20")),
        Row("w1", WorkOrder, ("wo_no", "WO-2401"), ("item_code", "I-100"), ("qty", "500"), ("note", "press line 2")),
        Row("w2", WorkOrder, ("wo_no", "WO-2402"), ("item_code", "I-300"), ("qty", "40"), ("note", "spindle rebuild")),
        Row("w3", WorkOrder, ("wo_no", "WO-2403"), ("item_code", "I-200"), ("qty", "500"), ("note", "press line 2")),
        Row("w4", WorkOrder, ("wo_no", "WO-2404"), ("item_code", "I-300"), ("qty", "12"), ("note", "conveyor")),
    ];

    private static DeclaredStructure[] Declared(bool keyed) =>
    [
        new(Supplier, [], []) { KeyField = keyed ? "supplier_id" : null },
        new(Item, [], [new DeclaredRelation("supplier", Supplier, ViaField: "supplier_id", Kind: DeclaredRelationKind.Reference)]) { KeyField = keyed ? "item_code" : null },
        new(WorkOrder, [], [new DeclaredRelation("item", Item, ViaField: "item_code", Kind: DeclaredRelationKind.Reference)]) { KeyField = keyed ? "wo_no" : null },
    ];

    private static LinkageAnalysis Analyze(bool keyed)
    {
        var declared = Declared(keyed);
        return LinkagePipeline.Analyze(
            LinkageEvidence.IdentityView(Records, declared),
            [],
            comparisonScope: LinkageEvidence.OwnershipScope(Records, declared),
            cannotLink: LinkageEvidence.DistinctByKey(Records, declared));
    }

    [Fact]
    public void Without_a_key_the_small_batch_pre_links_two_orders_that_only_share_a_quantity_and_a_note()
    {
        var pair = Assert.Single(Analyze(keyed: false).PairLinkages, p => p.Classification == LinkageClassification.Match);
        Assert.Equal(("w1", "w3"), (pair.RecordIdA, pair.RecordIdB));
    }

    [Fact]
    public void With_the_key_declared_rows_of_one_table_with_different_keys_are_not_compared()
    {
        var analysis = Analyze(keyed: true);

        Assert.DoesNotContain(analysis.PairLinkages, p => p.Classification == LinkageClassification.Match);
        Assert.DoesNotContain(analysis.PairLinkages, p => (p.RecordIdA, p.RecordIdB) == ("w1", "w3"));
        Assert.All(analysis.Clustering.Clusters, c => Assert.Single(c.RecordIds));
    }

    [Fact]
    public void Two_rows_with_the_same_key_are_still_compared_and_rows_of_different_tables_are_compared_as_before()
    {
        var duplicate = Row("w1b", WorkOrder, ("wo_no", " wo-2401 "), ("item_code", "I-100"), ("qty", "500"), ("note", "press line 2"));
        var distinct = LinkageEvidence.DistinctByKey([.. Records, duplicate], Declared(keyed: true));

        Assert.False(distinct(Records[5], duplicate));
        Assert.True(distinct(Records[5], Records[7]));
        Assert.False(distinct(Records[0], Records[2]));
    }

    [Fact]
    public void A_record_without_a_subject_or_a_value_in_the_key_is_known_distinct_from_nothing()
    {
        var unsubjected = new RawRecord("x", new Dictionary<string, string?> { ["wo_no"] = "WO-9" });
        var blank = Row("w5", WorkOrder, ("wo_no", " "), ("qty", "500"));
        var distinct = LinkageEvidence.DistinctByKey([.. Records, unsubjected, blank], Declared(keyed: true));

        Assert.False(distinct(Records[5], unsubjected));
        Assert.False(distinct(Records[5], blank));
    }

    [Fact]
    public void The_key_is_read_from_the_records_as_sampled_even_when_their_identity_view_no_longer_carries_it()
    {
        // A field that is both the subject's key and, by declaration, a reference is stripped from the
        // identity view — the pair is still known distinct by the key the records were sampled with.
        DeclaredStructure[] declared =
        [
            new(WorkOrder, [], [new DeclaredRelation("self", WorkOrder, ViaField: "wo_no", Kind: DeclaredRelationKind.Reference)]) { KeyField = "wo_no" },
        ];
        var view = LinkageEvidence.IdentityView(Records, declared);
        var distinct = LinkageEvidence.DistinctByKey(Records, declared);

        Assert.DoesNotContain("wo_no", view[5].Fields.Keys);
        Assert.True(distinct(view[5], view[7]));
    }

    [Fact]
    public async Task The_proposer_passes_the_cannot_link_to_its_pre_filter_and_tells_the_model_the_key()
    {
        var model = new StubModelClient("""{"entities":[],"relations":[]}""");

        var proposal = await new SinglePassOntologyProposer(model).ProposeAsync(Declared(keyed: true), Records, TestContext.Current.CancellationToken);

        var linkage = Assert.IsType<Eyu.Core.Proposals.LinkageReport>(proposal.Linkage);
        Assert.DoesNotContain(linkage.Analysis.PairLinkages, p => p.Classification == LinkageClassification.Match);
        Assert.Contains("Declared type: work_order; key field wo_no: two work_order records with different values in it are different entities", model.LastPrompt);
    }

    [Fact]
    public async Task A_blank_key_field_is_refused_before_the_model_is_called()
    {
        var model = new StubModelClient("""{"entities":[],"relations":[]}""");
        DeclaredStructure[] declared = [new(WorkOrder, [], []) { KeyField = "  " }];

        await Assert.ThrowsAsync<ArgumentException>(() => new SinglePassOntologyProposer(model).ProposeAsync(declared, Records, TestContext.Current.CancellationToken));
        Assert.Null(model.LastRequest);
    }
}
