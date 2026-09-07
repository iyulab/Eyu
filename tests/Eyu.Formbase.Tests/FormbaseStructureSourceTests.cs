using Eyu.Core.Declared;
using Eyu.Core.Primitives;
using Formbase.Core.InMemory;
using Formbase.Core.Primitives;
using Formbase.Core.Schema;
using Xunit;

namespace Eyu.Formbase.Tests;

public class FormbaseStructureSourceTests
{
    [Fact]
    public async Task GetStructureAsync_returns_null_when_the_subject_has_no_declared_hints()
    {
        var hintSource = new InMemoryFieldHintSource();
        var source = new FormbaseStructureSource(hintSource);

        var structure = await source.GetStructureAsync(SubjectRef.Create("invoice"), TestContext.Current.CancellationToken);

        Assert.Null(structure);
    }

    [Fact]
    public async Task GetStructureAsync_carries_the_declared_field_names_through_unchanged()
    {
        var hintSource = new InMemoryFieldHintSource();
        hintSource.Declare(new FormTypeHints(
            FormTypeRef.Create("invoice"),
            TableName: "invoice",
            Fields:
            [
                new FieldHint("lot", ColumnType.Text, Nullable: false),
                new FieldHint("qty", ColumnType.Integer),
            ]));
        var source = new FormbaseStructureSource(hintSource);

        var structure = await source.GetStructureAsync(SubjectRef.Create("invoice"), TestContext.Current.CancellationToken);

        Assert.NotNull(structure);
        Assert.Equal(["lot", "qty"], structure!.Fields.Select(f => f.Name));
    }

    [Fact]
    public async Task GetStructureAsync_carries_declared_relations_with_a_mapped_target_subject()
    {
        var hintSource = new InMemoryFieldHintSource();
        hintSource.Declare(new FormTypeHints(
            FormTypeRef.Create("invoice"),
            TableName: "invoice",
            Fields: [new FieldHint("lot", ColumnType.Text)],
            Relations: [new RelationHint("customer", RelationKind.Reference, FormTypeRef.Create("customer"), "customer_id")]));
        var source = new FormbaseStructureSource(hintSource);

        var structure = await source.GetStructureAsync(SubjectRef.Create("invoice"), TestContext.Current.CancellationToken);

        Assert.NotNull(structure);
        var relation = Assert.Single(structure!.Relations);
        Assert.Equal("customer", relation.Name);
        Assert.Equal(SubjectRef.Create("customer"), relation.Target);
    }

    [Fact]
    public async Task GetStructureAsync_carries_each_declared_value_type_in_Eyu_vocabulary()
    {
        // Every Formbase column type has to arrive as a declared kind -- a type the adapter cannot
        // carry would be a declared fact silently dropped, which is the defect this test pins.
        var hintSource = new InMemoryFieldHintSource();
        hintSource.Declare(new FormTypeHints(
            FormTypeRef.Create("work_order"),
            TableName: "work_order",
            Fields:
            [
                new FieldHint("wo_no", ColumnType.Text),
                new FieldHint("qty", ColumnType.Integer),
                new FieldHint("amount", ColumnType.Decimal),
                new FieldHint("urgent", ColumnType.Boolean),
                new FieldHint("reported_at", ColumnType.Timestamp),
                new FieldHint("asset_id", ColumnType.Uuid),
                new FieldHint("payload", ColumnType.Jsonb),
            ]));
        var source = new FormbaseStructureSource(hintSource);

        var structure = await source.GetStructureAsync(SubjectRef.Create("work_order"), TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                DeclaredValueKind.Text, DeclaredValueKind.WholeNumber, DeclaredValueKind.FractionalNumber, DeclaredValueKind.Boolean,
                DeclaredValueKind.Timestamp, DeclaredValueKind.Identifier, DeclaredValueKind.Structured,
            ],
            structure!.Fields.Select(f => f.Kind!.Value));
        Assert.Equal(
            Enum.GetValues<ColumnType>().Length,
            structure.Fields.Count);
    }

    [Fact]
    public async Task GetStructureAsync_carries_whether_a_field_is_required()
    {
        var hintSource = new InMemoryFieldHintSource();
        hintSource.Declare(new FormTypeHints(
            FormTypeRef.Create("work_order"),
            TableName: "work_order",
            Fields:
            [
                new FieldHint("wo_no", ColumnType.Text, Nullable: false),
                new FieldHint("qty", ColumnType.Integer),
            ]));
        var source = new FormbaseStructureSource(hintSource);

        var structure = await source.GetStructureAsync(SubjectRef.Create("work_order"), TestContext.Current.CancellationToken);

        Assert.Equal(true, structure!.Fields.Single(f => f.Name == "wo_no").Required);
        Assert.Equal(false, structure.Fields.Single(f => f.Name == "qty").Required);
    }

    [Fact]
    public async Task GetStructureAsync_carries_the_relation_kind_and_the_field_that_realises_it()
    {
        // "asset -> asset" alone tells a model nothing about which record field *is* the relation;
        // Formbase knows (the key field and its side), so that knowledge has to arrive.
        var hintSource = new InMemoryFieldHintSource();
        hintSource.Declare(new FormTypeHints(
            FormTypeRef.Create("work_order"),
            TableName: "work_order",
            Fields: [new FieldHint("wo_no", ColumnType.Text)],
            Relations:
            [
                new RelationHint("asset", RelationKind.Reference, FormTypeRef.Create("asset"), "asset_tag"),
                new RelationHint("tasks", RelationKind.Child, FormTypeRef.Create("task"), "work_order_id"),
            ]));
        var source = new FormbaseStructureSource(hintSource);

        var structure = await source.GetStructureAsync(SubjectRef.Create("work_order"), TestContext.Current.CancellationToken);

        var reference = structure!.Relations.Single(r => r.Name == "asset");
        Assert.Equal(DeclaredRelationKind.Reference, reference.Kind);
        Assert.Equal("asset_tag", reference.ViaField);

        var child = structure.Relations.Single(r => r.Name == "tasks");
        Assert.Equal(DeclaredRelationKind.Child, child.Kind);
        Assert.Equal("work_order_id", child.ViaField);
    }

    [Fact]
    public async Task GetStructureAsync_reports_a_missing_relations_list_as_no_relations()
    {
        var hintSource = new InMemoryFieldHintSource();
        hintSource.Declare(new FormTypeHints(
            FormTypeRef.Create("invoice"),
            TableName: "invoice",
            Fields: [new FieldHint("lot", ColumnType.Text)]));
        var source = new FormbaseStructureSource(hintSource);

        var structure = await source.GetStructureAsync(SubjectRef.Create("invoice"), TestContext.Current.CancellationToken);

        Assert.NotNull(structure);
        Assert.Empty(structure!.Relations);
    }

    [Fact]
    public async Task GetStructureAsync_carries_the_declaration_version()
    {
        var hintSource = new InMemoryFieldHintSource();
        hintSource.Declare(new FormTypeHints(
            FormTypeRef.Create("invoice"),
            TableName: "invoice",
            Fields: [new FieldHint("lot", ColumnType.Text)],
            DeclarationVersion: 3));
        var source = new FormbaseStructureSource(hintSource);

        var structure = await source.GetStructureAsync(SubjectRef.Create("invoice"), TestContext.Current.CancellationToken);

        Assert.Equal("3", structure!.Version);
    }
}
