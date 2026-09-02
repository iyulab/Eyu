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

        var structure = await source.GetStructureAsync(SubjectRef.Create("invoice"));

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

        var structure = await source.GetStructureAsync(SubjectRef.Create("invoice"));

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

        var structure = await source.GetStructureAsync(SubjectRef.Create("invoice"));

        Assert.NotNull(structure);
        var relation = Assert.Single(structure!.Relations);
        Assert.Equal("customer", relation.Name);
        Assert.Equal(SubjectRef.Create("customer"), relation.Target);
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

        var structure = await source.GetStructureAsync(SubjectRef.Create("invoice"));

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

        var structure = await source.GetStructureAsync(SubjectRef.Create("invoice"));

        Assert.Equal("3", structure!.Version);
    }
}
