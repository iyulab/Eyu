using System.Text;
using Eyu.Core.Declared;
using Eyu.Core.Inference;
using Eyu.Core.Judgment;
using Eyu.Core.Ports;
using Eyu.Core.Primitives;
using Eyu.Core.Proposals;
using Eyu.Core.Records;
using Xunit;

namespace Eyu.Core.Tests.Judgment;

/// <summary>
/// "Declared always wins" as something a caller can rely on rather than something the prompt asks
/// for. Each test drives the real proposer with a stub model that answers exactly what a
/// dogfooding run observed a model answering, and checks what comes out the other side.
/// </summary>
public class DeclaredStructureMergeTests
{
    private static readonly DeclaredStructure WorkOrder = new(
        SubjectRef.Create("work_order"),
        Fields: [new DeclaredField("wo_no", Kind: DeclaredValueKind.Text, Required: true)],
        Relations: [new DeclaredRelation("asset", SubjectRef.Create("asset"), ViaField: "asset_tag", Kind: DeclaredRelationKind.Reference)]);

    private static readonly RawRecord[] Records =
    [
        new("d1", new Dictionary<string, string?> { ["wo_no"] = "WO-1", ["asset_tag"] = "P-77" }),
    ];

    private const string ModelAnswer = """
        {
          "entities": [
            {"id": "e1", "name": "e1-name", "type": "WorkOrder", "claim": "d1 is a work order", "sources": ["d1"], "confidence": 0.9},
            {"id": "e2", "name": "e2-name", "type": "Technician", "claim": "kim is a technician", "sources": ["d1"], "confidence": 0.8},
            {"id": "e3", "name": "e3-name", "type": "Asset", "claim": "P-77 is an asset", "sources": ["d1"], "confidence": 0.9}
          ],
          "relations": [
            {"name": "asset", "from": "e1", "to": "e3", "claim": "d1 references P-77", "sources": ["d1"], "confidence": 0.7},
            {"name": "assigned_to", "from": "e1", "to": "e2", "claim": "kim is assigned", "sources": ["d1"], "confidence": 0.6}
          ]
        }
        """;

    [Fact]
    public async Task A_declared_relation_and_the_subject_entity_are_stamped_Declared_whatever_the_model_claimed()
    {
        var proposal = await Propose(ModelAnswer, WorkOrder);

        Assert.Equal(ProposalBasis.Declared, proposal.Entities.Single(e => e.EntityId == "e1").Basis);
        var asset = proposal.Relations.Single(r => r.RelationName == "asset");
        Assert.Equal(ProposalBasis.Declared, asset.Basis);
        Assert.Equal(VocabularyOrigin.Acquired, asset.Origin); // origin is the innate vocabulary's call, not the declaration's
        Assert.Equal(0.7, asset.Confidence);
    }

    [Fact]
    public async Task What_nothing_declared_stays_Inferred()
    {
        var proposal = await Propose(ModelAnswer, WorkOrder);

        Assert.Equal(ProposalBasis.Inferred, proposal.Entities.Single(e => e.EntityId == "e2").Basis);
        Assert.Equal(ProposalBasis.Inferred, proposal.Entities.Single(e => e.EntityId == "e3").Basis);
        Assert.Equal(ProposalBasis.Inferred, proposal.Relations.Single(r => r.RelationName == "assigned_to").Basis);
    }

    [Fact]
    public async Task A_relation_that_uses_a_declared_name_for_the_wrong_target_is_dropped()
    {
        // "asset" is declared to reach an asset. A model that hangs the name on the technician has
        // not found the declared relation; it has misused its name, and the declaration wins.
        const string answer = """
            {
              "entities": [
                {"id": "e1", "name": "e1-name", "type": "work_order", "claim": "d1", "sources": ["d1"], "confidence": 0.9},
                {"id": "e2", "name": "e2-name", "type": "Technician", "claim": "kim", "sources": ["d1"], "confidence": 0.8}
              ],
              "relations": [
                {"name": "asset", "from": "e1", "to": "e2", "claim": "misused", "sources": ["d1"], "confidence": 0.7}
              ]
            }
            """;

        var proposal = await Propose(answer, WorkOrder);

        Assert.Empty(proposal.Relations);
        Assert.Equal(2, proposal.Entities.Count);
        var rejection = Assert.Single(proposal.Rejections);
        Assert.Equal(ProposalElement.Relation, rejection.Element);
        Assert.Equal("asset", rejection.Id);
        Assert.Equal(RejectionReason.ContradictsDeclaration, rejection.Reason);
    }

    [Fact]
    public async Task A_relation_that_uses_a_declared_name_from_the_wrong_subject_is_dropped()
    {
        const string answer = """
            {
              "entities": [
                {"id": "e2", "name": "e2-name", "type": "Technician", "claim": "kim", "sources": ["d1"], "confidence": 0.8},
                {"id": "e3", "name": "e3-name", "type": "Asset", "claim": "P-77", "sources": ["d1"], "confidence": 0.9}
              ],
              "relations": [
                {"name": "asset", "from": "e2", "to": "e3", "claim": "technician has asset", "sources": ["d1"], "confidence": 0.7}
              ]
            }
            """;

        var proposal = await Propose(answer, WorkOrder);

        Assert.Empty(proposal.Relations);
        Assert.Equal(RejectionReason.ContradictsDeclaration, Assert.Single(proposal.Rejections).Reason);
    }

    [Fact]
    public async Task An_end_that_names_no_proposed_entity_is_left_out_before_the_merge_has_to_judge_it()
    {
        // The merge used to let such an end through as "cannot be checked". It never reaches the
        // merge now: a relation to an entity the response did not propose is left out at parse time
        // and reported as dangling, not as a contradiction the merge found.
        const string answer = """
            {
              "entities": [
                {"id": "e1", "name": "e1-name", "type": "work_order", "claim": "d1", "sources": ["d1"], "confidence": 0.9}
              ],
              "relations": [
                {"name": "asset", "from": "e1", "to": "missing", "claim": "d1 references something", "sources": ["d1"], "confidence": 0.5}
              ]
            }
            """;

        var proposal = await Propose(answer, WorkOrder);

        Assert.Empty(proposal.Relations);
        Assert.Single(proposal.Entities);
        var rejection = Assert.Single(proposal.Rejections);
        Assert.Equal(RejectionReason.DanglingRelationEnd, rejection.Reason);
        Assert.Contains("missing", rejection.Detail);
    }

    [Fact]
    public async Task Names_are_matched_across_naming_conventions()
    {
        var declared = new DeclaredStructure(
            SubjectRef.Create("work-order"),
            Fields: [],
            Relations: [new DeclaredRelation("AssignedTo", SubjectRef.Create("technician"))]);
        const string answer = """
            {
              "entities": [
                {"id": "e1", "name": "e1-name", "type": "WorkOrder", "claim": "d1", "sources": ["d1"], "confidence": 0.9},
                {"id": "e2", "name": "e2-name", "type": "technician", "claim": "kim", "sources": ["d1"], "confidence": 0.8}
              ],
              "relations": [
                {"name": "assigned_to", "from": "e1", "to": "e2", "claim": "kim is assigned", "sources": ["d1"], "confidence": 0.6}
              ]
            }
            """;

        var proposal = await Propose(answer, declared);

        Assert.Equal(ProposalBasis.Declared, proposal.Entities.Single(e => e.EntityId == "e1").Basis);
        Assert.Equal(ProposalBasis.Declared, Assert.Single(proposal.Relations).Basis);
    }

    [Fact]
    public async Task Names_are_matched_across_unicode_normalization_forms()
    {
        // Hangul written decomposed (NFD — how text saved on macOS commonly arrives) renders exactly
        // like the precomposed form a model answers in; a declaration must match it all the same.
        var declared = new DeclaredStructure(
            SubjectRef.Create("작업지시".Normalize(NormalizationForm.FormD)),
            Fields: [],
            Relations: [new DeclaredRelation("담당".Normalize(NormalizationForm.FormD), SubjectRef.Create("기술자".Normalize(NormalizationForm.FormD)))]);
        const string answer = """
            {
              "entities": [
                {"id": "e1", "name": "e1-name", "type": "작업지시", "claim": "d1", "sources": ["d1"], "confidence": 0.9},
                {"id": "e2", "name": "e2-name", "type": "기술자", "claim": "kim", "sources": ["d1"], "confidence": 0.8}
              ],
              "relations": [
                {"name": "담당", "from": "e1", "to": "e2", "claim": "kim is assigned", "sources": ["d1"], "confidence": 0.6}
              ]
            }
            """;

        var proposal = await Propose(answer, declared);

        Assert.Equal(ProposalBasis.Declared, proposal.Entities.Single(e => e.EntityId == "e1").Basis);
        Assert.Equal(ProposalBasis.Declared, Assert.Single(proposal.Relations).Basis);
    }

    [Fact]
    public async Task Without_declared_structure_nothing_is_stamped_and_nothing_is_dropped()
    {
        var proposal = await Propose(ModelAnswer);

        Assert.All(proposal.Entities, e => Assert.Equal(ProposalBasis.Inferred, e.Basis));
        Assert.All(proposal.Relations, r => Assert.Equal(ProposalBasis.Inferred, r.Basis));
        Assert.Equal(2, proposal.Relations.Count);
        Assert.Empty(proposal.Rejections);
    }

    // A document names many kinds of thing at once, so a caller that knows its vocabulary declares
    // each type as its own subject -- by name alone when it knows nothing more.
    private static readonly DeclaredStructure Organization = new(
        SubjectRef.Create("Organization"),
        Fields: [],
        Relations: [new DeclaredRelation("PartnerOf", SubjectRef.Create("Organization"))]);

    private static readonly DeclaredStructure Person = new(
        SubjectRef.Create("Person"),
        Fields: [],
        Relations: [new DeclaredRelation("EmployedBy", SubjectRef.Create("Organization"))]);

    private const string DocumentAnswer = """
        {
          "entities": [
            {"id": "e1", "name": "Hanbit Tech", "type": "Organization", "claim": "d1 names Hanbit Tech", "sources": ["d1"], "confidence": 0.9},
            {"id": "e2", "name": "Nuri Systems", "type": "Company", "claim": "d1 names Nuri Systems", "sources": ["d1"], "confidence": 0.8},
            {"id": "e3", "name": "Kim", "type": "person", "claim": "d1 names Kim", "sources": ["d1"], "confidence": 0.9},
            {"id": "e4", "name": "Aurora", "type": "Product", "claim": "d1 names Aurora", "sources": ["d1"], "confidence": 0.7}
          ],
          "relations": [
            {"name": "employed_by", "from": "e3", "to": "e1", "claim": "Kim works at Hanbit Tech", "sources": ["d1"], "confidence": 0.8},
            {"name": "Sells", "from": "e1", "to": "e4", "claim": "Hanbit Tech sells Aurora", "sources": ["d1"], "confidence": 0.7}
          ]
        }
        """;

    [Fact]
    public async Task Every_declared_type_is_stamped_Declared_and_undeclared_types_are_neither_filtered_nor_renamed()
    {
        var proposal = await Propose(DocumentAnswer, Organization, Person);

        Assert.Equal(ProposalBasis.Declared, proposal.Entities.Single(e => e.EntityId == "e1").Basis);
        Assert.Equal(ProposalBasis.Declared, proposal.Entities.Single(e => e.EntityId == "e3").Basis);

        // A declaration is a floor, not a ceiling: what it does not name comes back as inferred,
        // and "Company" is not folded into the declared "Organization" (no term normalization).
        var company = proposal.Entities.Single(e => e.EntityId == "e2");
        Assert.Equal(ProposalBasis.Inferred, company.Basis);
        Assert.Equal("Company", company.EntityType);
        Assert.Equal(ProposalBasis.Inferred, proposal.Entities.Single(e => e.EntityId == "e4").Basis);
        Assert.Equal(ProposalBasis.Inferred, proposal.Relations.Single(r => r.RelationName == "Sells").Basis);
        Assert.Equal(4, proposal.Entities.Count);
        Assert.Empty(proposal.Rejections);
    }

    [Fact]
    public async Task A_relation_declared_on_another_subject_is_checked_against_that_subjects_ends()
    {
        var proposal = await Propose(DocumentAnswer, Organization, Person);

        Assert.Equal(ProposalBasis.Declared, proposal.Relations.Single(r => r.RelationName == "employed_by").Basis);
    }

    [Fact]
    public async Task A_relation_name_several_subjects_declare_fits_when_it_matches_any_one_of_them()
    {
        var component = new DeclaredStructure(SubjectRef.Create("Component"), [], [new DeclaredRelation("PartOf", SubjectRef.Create("Assembly"))]);
        var department = new DeclaredStructure(SubjectRef.Create("Department"), [], [new DeclaredRelation("PartOf", SubjectRef.Create("Organization"))]);
        const string answer = """
            {
              "entities": [
                {"id": "e1", "name": "Sales", "type": "Department", "claim": "d1", "sources": ["d1"], "confidence": 0.9},
                {"id": "e2", "name": "Hanbit Tech", "type": "Organization", "claim": "d1", "sources": ["d1"], "confidence": 0.9},
                {"id": "e3", "name": "Pump", "type": "Component", "claim": "d1", "sources": ["d1"], "confidence": 0.9}
              ],
              "relations": [
                {"name": "PartOf", "from": "e1", "to": "e2", "claim": "Sales is part of Hanbit Tech", "sources": ["d1"], "confidence": 0.8},
                {"name": "PartOf", "from": "e3", "to": "e2", "claim": "the pump is part of Hanbit Tech", "sources": ["d1"], "confidence": 0.6}
              ]
            }
            """;

        var proposal = await Propose(answer, component, department);

        var kept = Assert.Single(proposal.Relations);
        Assert.Equal("e1", kept.FromEntityId);
        Assert.Equal(ProposalBasis.Declared, kept.Basis);
        var rejection = Assert.Single(proposal.Rejections);
        Assert.Equal(RejectionReason.ContradictsDeclaration, rejection.Reason);
        Assert.Contains("Component -> Assembly, Department -> Organization", rejection.Detail);
    }

    [Fact]
    public async Task The_same_subject_declared_twice_is_refused_before_the_model_is_called()
    {
        var model = new CountingModelClient();
        var again = new DeclaredStructure(SubjectRef.Create("work-order"), [], []);

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => new SinglePassOntologyProposer(model).ProposeAsync([WorkOrder, again], Records, TestContext.Current.CancellationToken));

        Assert.Contains("\"work_order\", \"work-order\"", error.Message);
        Assert.Equal(0, model.Calls);
    }

    private static Task<OntologyProposal> Propose(string modelAnswer, params DeclaredStructure[] declared)
        => new SinglePassOntologyProposer(new StubModelClient(modelAnswer)).ProposeAsync(declared, Records);

    private sealed class StubModelClient(string responseText) : IModelClient
    {
        public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ModelResponse(responseText));
    }

    private sealed class CountingModelClient : IModelClient
    {
        public int Calls { get; private set; }

        public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ModelResponse("""{"entities":[],"relations":[]}"""));
        }
    }
}
