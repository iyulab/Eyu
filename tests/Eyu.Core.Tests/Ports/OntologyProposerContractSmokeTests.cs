using Eyu.Core.Declared;
using Eyu.Core.Grounding;
using Eyu.Core.Ports;
using Eyu.Core.Primitives;
using Eyu.Core.Proposals;
using Eyu.Core.Records;
using Xunit;

namespace Eyu.Core.Tests.Ports;

/// <summary>
/// Same role as <c>PortContractSmokeTests</c>: proves the <see cref="IOntologyProposer"/> type
/// surface resolves and runs via a minimal stub, not a design commitment about a real judgment
/// engine. The stub returns a fixed proposal; it does no actual entity resolution or inference —
/// that internal engine is separate scope, gated on the A/B/C/D benchmark decision (ROADMAP.md).
/// </summary>
public class OntologyProposerContractSmokeTests
{
    [Fact]
    public async Task IOntologyProposer_stub_proposes_entities_and_relations_from_records()
    {
        IOntologyProposer proposer = new StubOntologyProposer();
        var subject = SubjectRef.Create("invoice");
        var structure = new DeclaredStructure(subject, Fields: [new DeclaredField("total")], Relations: []);
        IReadOnlyList<RawRecord> records =
        [
            new("rec-1", new Dictionary<string, string?> { ["total"] = "100" }),
        ];

        var proposal = await proposer.ProposeAsync(structure, records, TestContext.Current.CancellationToken);

        Assert.Single(proposal.Entities);
        Assert.Single(proposal.Relations);
        Assert.Equal(VocabularyOrigin.Innate, proposal.Entities[0].Origin);
    }

    [Fact]
    public async Task IOntologyProposer_stub_accepts_records_with_no_declared_structure()
    {
        // README: caller hands declared structure "and/or" raw records — structure may be absent.
        IOntologyProposer proposer = new StubOntologyProposer();
        IReadOnlyList<RawRecord> records = [new("rec-1", new Dictionary<string, string?>())];

        var proposal = await proposer.ProposeAsync(declaredStructure: null, records, TestContext.Current.CancellationToken);

        Assert.NotNull(proposal);
    }

    private sealed class StubOntologyProposer : IOntologyProposer
    {
        public Task<OntologyProposal> ProposeAsync(DeclaredStructure? declaredStructure, IReadOnlyList<RawRecord> records, CancellationToken cancellationToken = default)
        {
            var claim = GroundedClaim.Create("rec-1 denotes an Invoice", sources: [new SourceRef("rec-1")]);
            var entity = EntityProposal.Create("e1", "Invoice", claim, VocabularyOrigin.Innate, confidence: 0.9);
            var relation = RelationProposal.Create("self", "e1", "e1", claim, VocabularyOrigin.Innate, confidence: 0.9);

            return Task.FromResult(new OntologyProposal(Entities: [entity], Relations: [relation]));
        }
    }
}
