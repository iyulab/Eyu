using Formbase.Core.Ports;
using Formbase.Core.Primitives;
using Formbase.Core.Schema;
using Xunit;

namespace Eyu.IntegrationSmoke.Tests;

/// <summary>
/// L1a↔L2 coupling proof: establishes that Eyu's codebase can
/// reference the published `Formbase.Core` package and satisfy its <see cref="ISchemaProposer"/>
/// port end to end.
/// <para>
/// Eyu's own architecture (IStructureSource / IRecordSample / IOntologyProposer /
/// IGroundingContract) is no longer an open question this test doesn't touch — all five ports are
/// implemented, and <c>SinglePassOntologyProposer</c>/<c>HttpModelClient</c> have been measured
/// against a real model (see README's Status line). That coverage lives in
/// <c>Eyu.Core.Tests</c> against a mock <c>IModelClient</c>, referenced via
/// <c>ProjectReference</c> rather than the package-boundary style this project exists for,
/// because `Eyu.Core` is not yet published — there is nothing on NuGet for a project restricted
/// to <c>PackageReference</c> (like this one) to point at. This test's own scope stays exactly
/// what its name says: the `Formbase.Core` coupling, not Eyu's ports.
/// </para>
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
