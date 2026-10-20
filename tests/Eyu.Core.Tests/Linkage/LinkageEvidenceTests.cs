using Eyu.Core.Declared;
using Eyu.Core.Inference;
using Eyu.Core.Judgment;
using Eyu.Core.Linkage;
using Eyu.Core.Ports;
using Eyu.Core.Primitives;
using Eyu.Core.Records;
using Xunit;

namespace Eyu.Core.Tests.Linkage;

/// <summary>
/// Inspection logs of one machine agree on the machine's tag, name and location — fields that name the
/// machine, not the log. Counted as identity evidence they pre-linked distinct logs (and the machine's
/// register entry) as one entity and told the model not to re-decide.
/// </summary>
public class LinkageEvidenceTests
{
    private static readonly SubjectRef Register = SubjectRef.Create("equipment_register");
    private static readonly SubjectRef Log = SubjectRef.Create("inspection_log");

    /// <summary>The register declares the log as its child through <c>asset_tag</c>; the log declares its reference to the register.</summary>
    private static readonly DeclaredStructure[] Declared =
    [
        new(Register, [new DeclaredField("asset_tag"), new DeclaredField("asset_name"), new DeclaredField("location")],
            [new DeclaredRelation("inspections", Log, ViaField: "asset_tag", Kind: DeclaredRelationKind.Child)]),
        new(Log, [new DeclaredField("asset_tag"), new DeclaredField("asset_name"), new DeclaredField("location")],
            [
                new DeclaredRelation("asset_name", Register, ViaField: "asset_name", Kind: DeclaredRelationKind.Reference),
                new DeclaredRelation("location", Register, ViaField: "location", Kind: DeclaredRelationKind.Reference),
            ]),
    ];

    private static RawRecord Machine(string id, SubjectRef? subject) =>
        new(id, new Dictionary<string, string?> { ["asset_tag"] = "P-3", ["asset_name"] = "Press 3", ["location"] = "Line A" }, subject);

    [Fact]
    public void A_logs_reference_fields_are_read_per_subject()
    {
        var references = LinkageEvidence.ReferenceFieldsBySubject(Declared);

        Assert.Equal(["asset_name", "asset_tag", "location"], references[Log].Order());
        Assert.False(references.ContainsKey(Register), "the register's asset_tag is its own key, not a reference");
    }

    [Fact]
    public void The_identity_view_drops_a_logs_references_and_keeps_a_registers_own_fields()
    {
        var view = LinkageEvidence.IdentityView([Machine("log-1", Log), Machine("reg-1", Register)], Declared);

        Assert.Empty(view[0].Fields);
        Assert.Equal(3, view[1].Fields.Count);
        Assert.Equal(Log, view[0].Subject);
    }

    [Fact]
    public void A_record_without_a_subject_keeps_every_field()
    {
        var record = Machine("log-1", subject: null);

        Assert.Same(record, LinkageEvidence.IdentityView([record], Declared)[0]);
    }

    [Fact]
    public void A_relation_that_does_not_say_its_side_is_not_read()
    {
        DeclaredStructure[] unsided =
        [
            new(Log, [new DeclaredField("asset_tag")], [new DeclaredRelation("of", Register, ViaField: "asset_tag")]),
        ];

        Assert.Empty(LinkageEvidence.ReferenceFieldsBySubject(unsided));
    }

    /// <summary>
    /// The defect end to end: without subjects the two logs are pre-linked as one entity (the positive
    /// control — the same records do trip the filter); with them, nothing is left to link them on.
    /// </summary>
    [Fact]
    public async Task Two_logs_of_one_machine_are_not_pre_linked_once_their_subject_is_known()
    {
        var control = new StubModelClient("""{"entities":[],"relations":[]}""");
        await new SinglePassOntologyProposer(control).ProposeAsync(
            Declared, [Machine("log-1", null), Machine("log-2", null)], TestContext.Current.CancellationToken);
        Assert.Contains("Pre-linked record groups", control.LastPrompt);

        var model = new StubModelClient("""{"entities":[],"relations":[]}""");
        var proposal = await new SinglePassOntologyProposer(model).ProposeAsync(
            Declared, [Machine("log-1", Log), Machine("log-2", Log)], TestContext.Current.CancellationToken);

        Assert.DoesNotContain("Pre-linked record groups", model.LastPrompt);
        Assert.DoesNotContain(proposal.Linkage!.Analysis.Clustering.Clusters, c => c.RecordIds.Count > 1);
    }

    [Fact]
    public async Task Two_register_entries_of_one_machine_are_still_pre_linked()
    {
        var model = new StubModelClient("""{"entities":[],"relations":[]}""");
        await new SinglePassOntologyProposer(model).ProposeAsync(
            Declared, [Machine("reg-1", Register), Machine("reg-2", Register)], TestContext.Current.CancellationToken);

        Assert.Contains("Pre-linked record groups", model.LastPrompt);
    }
}
