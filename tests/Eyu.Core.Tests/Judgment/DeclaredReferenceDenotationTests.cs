using Eyu.Core.Declared;
using Eyu.Core.Inference;
using Eyu.Core.Judgment;
using Eyu.Core.Linkage;
using Eyu.Core.Ports;
using Eyu.Core.Primitives;
using Eyu.Core.Proposals;
using Eyu.Core.Records;
using Xunit;

namespace Eyu.Core.Tests.Judgment;

// A field the caller declared as carrying a relation (DeclaredRelation.ViaField) holds the key of the
// relation's other end, not a value of the record's own thing. A record that holds an entity's name in
// such a field mentions that entity; it does not denote it -- whatever the answer put under "denotedBy".
public class DeclaredReferenceDenotationTests
{
    // Work orders name their machine in "machine"; the caller says so.
    private static readonly DeclaredStructure WorkOrderDeclaration = new(
        SubjectRef.Create("work_order"),
        [new DeclaredField("order_no"), new DeclaredField("machine")],
        [new DeclaredRelation("uses_machine", SubjectRef.Create("machine"), ViaField: "machine", DeclaredRelationKind.Reference)]);

    [Fact]
    public async Task A_record_naming_an_entity_in_a_declared_reference_field_is_demoted_to_a_mention()
    {
        var model = new StubModelClient(Response(
            Entity("wo1", "WO-2024-0311", "WorkOrder", ["w-01"], ["w-01"]),
            Entity("wo2", "WO-2024-0312", "WorkOrder", ["w-02"], ["w-02"]),
            Entity("press", "Press 3", "Machine", ["w-01", "w-02", "m-01"], ["w-01", "w-02", "m-01"])));

        var proposal = await new SinglePassOntologyProposer(model).ProposeAsync([WorkOrderDeclaration], WorkOrdersAndMachine(), TestContext.Current.CancellationToken);

        var press = proposal.Entities.Single(e => e.EntityId == "press");
        Assert.Equal(["m-01"], press.DenotedBy);
        Assert.Equal(["w-01", "w-02"], press.MentionedIn);
        Assert.Equal(["w-01", "w-02", "m-01"], press.Claim.Sources.Select(s => s.RecordId));
        Assert.Equal(
            [new DemotedDenotation("press", "w-01", "machine"), new DemotedDenotation("press", "w-02", "machine")],
            proposal.DemotedDenotations);
        Assert.Empty(proposal.Rejections);
    }

    // The work orders' own entities keep their records: "order_no" is not a declared reference field.
    [Fact]
    public async Task An_entity_named_by_a_field_no_relation_runs_through_keeps_its_record()
    {
        var model = new StubModelClient(Response(
            Entity("wo1", "WO-2024-0311", "WorkOrder", ["w-01"], ["w-01"]),
            Entity("press", "Press 3", "Machine", ["w-01", "m-01"], ["w-01", "m-01"])));

        var proposal = await new SinglePassOntologyProposer(model).ProposeAsync([WorkOrderDeclaration], WorkOrdersAndMachine(), TestContext.Current.CancellationToken);

        Assert.Equal(["w-01"], proposal.Entities.Single(e => e.EntityId == "wo1").DenotedBy);
        Assert.Equal(["m-01"], proposal.Entities.Single(e => e.EntityId == "press").DenotedBy);
    }

    // Demoting the work orders is what lifts the linkage penalty: the pre-filter found the two work
    // orders different, so a machine "denoted" by both read as a contradiction.
    [Fact]
    public async Task The_demotion_is_applied_before_linkage_adjusts_confidence()
    {
        var model = new StubModelClient(Response(
            Entity("wo1", "WO-2024-0311", "WorkOrder", ["w-01"], ["w-01"]),
            Entity("wo2", "WO-2024-0312", "WorkOrder", ["w-02"], ["w-02"]),
            Entity("press", "Press 3", "Machine", ["w-01", "w-02", "m-01"], ["w-01", "w-02"])));

        var undeclared = await new SinglePassOntologyProposer(model).ProposeAsync([], WorkOrdersAndMachine(), TestContext.Current.CancellationToken);
        var declared = await new SinglePassOntologyProposer(model).ProposeAsync([WorkOrderDeclaration], WorkOrdersAndMachine(), TestContext.Current.CancellationToken);

        Assert.True(undeclared.Entities.Single(e => e.EntityId == "press").Confidence < 0.9);
        var press = declared.Entities.Single(e => e.EntityId == "press");
        Assert.Empty(press.DenotedBy);
        Assert.Equal(0.9, press.Confidence);
    }

    // Without a declaration nothing says which field is a reference, so nothing is demoted.
    [Fact]
    public async Task Nothing_is_demoted_without_a_declared_reference_field()
    {
        var model = new StubModelClient(Response(
            Entity("wo1", "WO-2024-0311", "WorkOrder", ["w-01"], ["w-01"]),
            Entity("press", "Press 3", "Machine", ["w-01", "m-01"], ["w-01", "m-01"])));
        var noRelation = new DeclaredStructure(SubjectRef.Create("work_order"), [new DeclaredField("machine")], []);

        var undeclared = await new SinglePassOntologyProposer(model).ProposeAsync([], WorkOrdersAndMachine(), TestContext.Current.CancellationToken);
        var fieldOnly = await new SinglePassOntologyProposer(model).ProposeAsync([noRelation], WorkOrdersAndMachine(), TestContext.Current.CancellationToken);

        foreach (var proposal in new[] { undeclared, fieldOnly })
        {
            Assert.Equal(["w-01", "m-01"], proposal.Entities.Single(e => e.EntityId == "press").DenotedBy);
            Assert.Empty(proposal.DemotedDenotations);
        }
    }

    // The comparison ignores case and surrounding or repeated whitespace -- the name is the field's
    // value as the answer wrote it, not a different thing.
    [Fact]
    public async Task The_name_matches_the_field_value_ignoring_case_and_whitespace()
    {
        var model = new StubModelClient(Response(
            Entity("wo1", "WO-2024-0311", "WorkOrder", ["w-01"], ["w-01"]),
            Entity("press", " press  3 ", "Machine", ["w-01", "m-01"], ["w-01", "m-01"])));

        var proposal = await new SinglePassOntologyProposer(model).ProposeAsync([WorkOrderDeclaration], WorkOrdersAndMachine(), TestContext.Current.CancellationToken);

        Assert.Equal(["m-01"], proposal.Entities.Single(e => e.EntityId == "press").DenotedBy);
    }

    // A different name is a different claim, and whether "Press No.3" is "Press 3" is not this check's
    // question -- known-entity matching and merge candidates answer that.
    [Fact]
    public async Task An_entity_whose_name_differs_from_the_field_value_keeps_its_record()
    {
        var model = new StubModelClient(Response(
            Entity("wo1", "WO-2024-0311", "WorkOrder", ["w-01"], ["w-01"]),
            Entity("press", "Press No.3", "Machine", ["w-01", "m-01"], ["w-01", "m-01"])));

        var proposal = await new SinglePassOntologyProposer(model).ProposeAsync([WorkOrderDeclaration], WorkOrdersAndMachine(), TestContext.Current.CancellationToken);

        Assert.Equal(["w-01", "m-01"], proposal.Entities.Single(e => e.EntityId == "press").DenotedBy);
        Assert.Empty(proposal.DemotedDenotations);
    }

    // A reference field can also be what names the record's own thing (an event whose title is also
    // the key of its cause). When the matched entity is the only one the record would denote, the
    // record is taken to be that entity's and nothing is demoted: a record denotes its own thing, and
    // demoting would leave this one denoting none.
    [Fact]
    public async Task A_record_left_denoting_nothing_keeps_its_only_entity()
    {
        var model = new StubModelClient(Response(
            Entity("wo1", "WO-2024-0311", "WorkOrder", ["w-01"], ["w-01"]),
            Entity("press", "Press 3", "Machine", ["w-01", "w-02", "m-01"], ["w-01", "w-02", "m-01"])));

        var proposal = await new SinglePassOntologyProposer(model).ProposeAsync([WorkOrderDeclaration], WorkOrdersAndMachine(), TestContext.Current.CancellationToken);

        Assert.Equal(["w-02", "m-01"], proposal.Entities.Single(e => e.EntityId == "press").DenotedBy);
        Assert.Equal([new DemotedDenotation("press", "w-01", "machine")], proposal.DemotedDenotations);
    }

    // Where records do not each denote one entity, no entity keeps a denoting record anyway; there is
    // nothing to demote and nothing is reported.
    [Fact]
    public async Task Nothing_is_reported_when_records_do_not_denote_entities()
    {
        var model = new StubModelClient(Response(
            Entity("wo1", "WO-2024-0311", "WorkOrder", ["w-01"], ["w-01"]),
            Entity("press", "Press 3", "Machine", ["w-01", "m-01"], ["w-01", "m-01"])));
        var proposal = await new SinglePassOntologyProposer(model, new LinkageOptions(RecordsDenoteEntities: false)).ProposeAsync([WorkOrderDeclaration], WorkOrdersAndMachine(), TestContext.Current.CancellationToken);

        Assert.All(proposal.Entities, e => Assert.Empty(e.DenotedBy));
        Assert.Empty(proposal.DemotedDenotations);
    }

    private static RawRecord[] WorkOrdersAndMachine() =>
    [
        new("w-01", new Dictionary<string, string?> { ["order_no"] = "WO-2024-0311", ["machine"] = "Press 3", ["operator"] = "Kim" }),
        new("w-02", new Dictionary<string, string?> { ["order_no"] = "WO-2024-0312", ["machine"] = "Press 3", ["operator"] = "Lee" }),
        new("m-01", new Dictionary<string, string?> { ["machine_name"] = "Press 3", ["maker"] = "Hanbit", ["rated_tons"] = "200" }),
    ];

    private static string Entity(string id, string name, string type, string[] sources, string[] denotedBy) =>
        $$"""{"id": "{{id}}", "name": "{{name}}", "type": "{{type}}", "claim": "x", "sources": [{{Quote(sources)}}], "denotedBy": [{{Quote(denotedBy)}}], "confidence": 0.9}""";

    private static string Response(params string[] entities) => $$"""{"entities": [{{string.Join(", ", entities)}}], "relations": []}""";

    private static string Quote(string[] ids) => string.Join(", ", ids.Select(id => $"\"{id}\""));

    private sealed class StubModelClient(string responseText) : IModelClient
    {
        public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ModelResponse(responseText));
    }
}
