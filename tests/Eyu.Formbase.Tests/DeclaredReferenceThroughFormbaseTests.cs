using Eyu.Core.Inference;
using Eyu.Core.Judgment;
using Eyu.Core.Ports;
using Eyu.Core.Primitives;
using Eyu.Core.Records;
using Formbase.Core.InMemory;
using Formbase.Core.Primitives;
using Formbase.Core.Schema;
using Xunit;

namespace Eyu.Formbase.Tests;

// A relation Formbase declares reaches the proposer as the field it runs through, on whichever side
// carries it: a reference's key field sits on the declaring type's records, a child's on the child's.
// Either way a work order holding its machine's name there mentions the machine and does not denote it.
public class DeclaredReferenceThroughFormbaseTests
{
    private const string Answer = """
        {"entities": [
          {"id": "wo1", "name": "WO-1", "type": "WorkOrder", "claim": "x", "sources": ["w-01"], "denotedBy": ["w-01"], "confidence": 0.9},
          {"id": "press", "name": "Press 3", "type": "Machine", "claim": "x", "sources": ["w-01", "m-01"], "denotedBy": ["w-01", "m-01"], "confidence": 0.9}
        ], "relations": []}
        """;

    private static readonly RawRecord[] Records =
    [
        new("w-01", new Dictionary<string, string?> { ["order_no"] = "WO-1", ["machine"] = "Press 3" }),
        new("m-01", new Dictionary<string, string?> { ["machine_name"] = "Press 3", ["maker"] = "Hanbit" }),
    ];

    [Fact]
    public async Task A_reference_declared_on_the_work_order_withdraws_the_work_orders_denotation_of_its_machine()
    {
        var hints = new InMemoryFieldHintSource();
        hints.Declare(new FormTypeHints(
            FormTypeRef.Create("work_order"),
            TableName: "work_order",
            Fields: [new FieldHint("order_no", ColumnType.Text), new FieldHint("machine", ColumnType.Text)],
            Relations: [new RelationHint("uses_machine", RelationKind.Reference, FormTypeRef.Create("machine"), "machine")]));

        var proposal = await ProposeWith(hints, "work_order");

        Assert.Equal(["m-01"], proposal.Entities.Single(e => e.EntityId == "press").DenotedBy);
        Assert.Equal("machine", Assert.Single(proposal.DemotedDenotations).Field);
    }

    [Fact]
    public async Task A_child_relation_declared_on_the_machine_withdraws_it_too()
    {
        var hints = new InMemoryFieldHintSource();
        hints.Declare(new FormTypeHints(
            FormTypeRef.Create("machine"),
            TableName: "machine",
            Fields: [new FieldHint("machine_name", ColumnType.Text), new FieldHint("maker", ColumnType.Text)],
            Relations: [new RelationHint("work_orders", RelationKind.Child, FormTypeRef.Create("work_order"), "machine")]));

        var proposal = await ProposeWith(hints, "machine");

        Assert.Equal(["m-01"], proposal.Entities.Single(e => e.EntityId == "press").DenotedBy);
        Assert.Equal(new Core.Proposals.DemotedDenotation("press", "w-01", "machine"), Assert.Single(proposal.DemotedDenotations));
    }

    // The same reference declared only as a bound field — the machine's name copied onto the work order
    // from the machine form type — withdraws the denotation just the same: before, the adapter carried
    // relations alone and a consumer that declared the reference this way got no withdrawal at all.
    [Fact]
    public async Task A_field_bound_to_the_machine_withdraws_the_work_orders_denotation_too()
    {
        var hints = new InMemoryFieldHintSource();
        hints.Declare(new FormTypeHints(
            FormTypeRef.Create("work_order"),
            TableName: "work_order",
            Fields:
            [
                new FieldHint("order_no", ColumnType.Text),
                new FieldHint("machine", ColumnType.Text, Binding: FieldBinding.Snapshot, Target: new EntityRef(FormTypeRef.Create("machine"), "machine_name")),
            ]));

        var proposal = await ProposeWith(hints, "work_order");

        Assert.Equal(["m-01"], proposal.Entities.Single(e => e.EntityId == "press").DenotedBy);
        Assert.Equal(new Core.Proposals.DemotedDenotation("press", "w-01", "machine"), Assert.Single(proposal.DemotedDenotations));
    }

    private static async Task<Core.Proposals.OntologyProposal> ProposeWith(InMemoryFieldHintSource hints, string subject)
    {
        var structure = await new FormbaseStructureSource(hints).GetStructureAsync(SubjectRef.Create(subject), TestContext.Current.CancellationToken);
        Assert.NotNull(structure);
        return await new SinglePassOntologyProposer(new StubModelClient(Answer)).ProposeAsync([structure!], Records, TestContext.Current.CancellationToken);
    }

    private sealed class StubModelClient(string responseText) : IModelClient
    {
        public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ModelResponse(responseText));
    }
}
