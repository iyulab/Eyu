using Formbase.Core.Ports;
using Formbase.Core.Primitives;
using Formbase.Core.Schema;
using Xunit;

namespace Eyu.IntegrationSmoke.Tests;

/// <summary>
/// L1a↔L2 coupling proof (BD-20260830-04, ROADMAP.md): before any of Eyu's own ports exist, this
/// establishes that Eyu's codebase can reference the published `Formbase.Core` package and satisfy
/// its <see cref="ISchemaProposer"/> port end to end. It says nothing about Eyu's eventual
/// architecture (IStructureSource / IRecordSample / IOntologyProposer / IGroundingContract) — those
/// stay open design questions this test does not touch.
/// </summary>
public class SchemaProposerCouplingSmokeTests
{
    [Fact]
    public async Task Eyu_side_implementation_satisfies_Formbase_Core_ISchemaProposer()
    {
        ISchemaProposer proposer = new StubProposer();
        var type = FormTypeRef.Create("invoice");

        var schema = await proposer.ProposeAsync(type);

        Assert.NotNull(schema);
        Assert.Equal("invoice", schema!.TableName);
        Assert.Single(schema.Columns);
        Assert.Equal("amount", schema.Columns[0].Name);
    }

    /// <summary>
    /// A placeholder, not a design commitment — real inference is Eyu's `IOntologyProposer`'s job
    /// once that port exists. This only proves the type surface resolves and runs across the
    /// package boundary.
    /// </summary>
    private sealed class StubProposer : ISchemaProposer
    {
        public Task<TableSchema?> ProposeAsync(FormTypeRef type, CancellationToken cancellationToken = default)
        {
            var schema = new TableSchema(
                type.Value,
                new[] { new ColumnDef("amount", ColumnType.Decimal) });

            return Task.FromResult<TableSchema?>(schema);
        }
    }
}
