using Eyu.Core.Declared;
using Eyu.Core.Grounding;
using Eyu.Core.Judgment;
using Eyu.Core.Primitives;
using Eyu.Core.Proposals;
using Eyu.Core.Records;
using Xunit;

namespace Eyu.Core.Tests.Judgment;

/// <summary>
/// Type relations: which types the records show to be kinds of others (<c>is-a</c>). The channel is asked
/// for only when the proposer is told to, or a declaration names a super type — otherwise the request is
/// the one a call always sent. Its ends are type names, not entities, and every one cites the records.
/// </summary>
public sealed class TypeRelationProposalTests
{
    private static readonly IReadOnlyList<RawRecord> Glossary =
    [
        new("g-1", new Dictionary<string, string?> { ["text"] = "A hydraulic press is a press that uses a hydraulic cylinder." }),
        new("g-2", new Dictionary<string, string?> { ["text"] = "Presses and conveyors are equipment of the line." }),
    ];

    private const string Answer = """
        {
          "entities": [
            {"id": "e1", "name": "press no. 4", "type": "HydraulicPress", "claim": "g-1 describes it", "sources": ["g-1"], "denotedBy": [], "confidence": 0.7}
          ],
          "relations": [],
          "typeRelations": [
            {"sub": "hydraulic press", "super": "Press", "claim": "g-1 says a hydraulic press is a press", "sources": ["g-1"], "confidence": 0.9},
            {"sub": "Press", "super": "Equipment", "claim": "g-2 calls presses equipment", "sources": ["g-2"], "confidence": 0.8},
            {"sub": "press", "super": "equipment", "claim": "the same pair again", "sources": ["g-2"], "confidence": 0.5},
            {"sub": "Conveyor", "super": "conveyor", "claim": "a conveyor is a conveyor", "sources": ["g-2"], "confidence": 0.5},
            {"sub": "Conveyor", "super": "Equipment", "claim": "uncited", "sources": [], "confidence": 0.5}
          ]
        }
        """;

    private static Task<OntologyProposal> Propose(string answer, IReadOnlyList<DeclaredStructure> declared, bool proposeTypeRelations = true, StubModelClient? model = null) =>
        new SinglePassOntologyProposer(model ?? new StubModelClient(answer), proposeTypeRelations: proposeTypeRelations)
            .ProposeAsync(declared, Glossary, TestContext.Current.CancellationToken);

    private static DeclaredStructure Declare(string subject, params string[] superTypes) =>
        new(SubjectRef.Create(subject), [], []) { SuperTypes = superTypes };

    [Fact]
    public async Task Not_asked_for_the_request_is_the_one_a_call_always_sent_and_an_answer_there_is_not_read()
    {
        var plain = new StubModelClient(Answer);
        var off = new StubModelClient(Answer);

        await new SinglePassOntologyProposer(plain).ProposeAsync([], Glossary, TestContext.Current.CancellationToken);
        var proposal = await Propose(Answer, [], proposeTypeRelations: false, model: off);

        Assert.Equal(plain.LastPrompt, off.LastPrompt);
        Assert.Equal(plain.LastRequest!.ResponseSchema!.Value.GetRawText(), off.LastRequest!.ResponseSchema!.Value.GetRawText());
        Assert.DoesNotContain("typeRelations", off.LastPrompt!, StringComparison.Ordinal);
        Assert.Empty(proposal.TypeRelations);
        Assert.DoesNotContain(proposal.Rejections, r => r.Element == ProposalElement.TypeRelation);
    }

    [Fact]
    public async Task An_unasked_type_relation_citing_an_unknown_record_does_not_refuse_the_answer()
    {
        const string answer = """{"entities": [], "relations": [], "typeRelations": [{"sub": "A", "super": "B", "claim": "x", "sources": ["nowhere"], "confidence": 0.5}]}""";

        var proposal = await Propose(answer, [], proposeTypeRelations: false);

        Assert.Empty(proposal.TypeRelations);
    }

    [Fact]
    public async Task Asked_for_the_prompt_offers_the_channel_and_the_schema_requires_it()
    {
        var model = new StubModelClient(Answer);

        await Propose(Answer, [], model: model);

        Assert.Contains("\"typeRelations\":[{\"sub\",\"super\",\"claim\",\"sources\",\"confidence\"}]", model.LastPrompt!, StringComparison.Ordinal);
        Assert.Contains("A type relation says that one type is a kind of another", model.LastPrompt!, StringComparison.Ordinal);
        var schema = model.LastRequest!.ResponseSchema!.Value;
        Assert.True(schema.GetProperty("properties").TryGetProperty("typeRelations", out _));
        Assert.Contains(schema.GetProperty("required").EnumerateArray(), e => e.GetString() == "typeRelations");
    }

    [Fact]
    public async Task The_preamble_stays_silent_about_which_kinds_become_types()
    {
        var plain = new StubModelClient(Answer);
        var asked = new StubModelClient(Answer);

        await new SinglePassOntologyProposer(plain).ProposeAsync([], Glossary, TestContext.Current.CancellationToken);
        await Propose(Answer, [], model: asked);

        // The channel adds its schema sentence and its clause, and changes nothing before them: the
        // opening instruction is the one every call sends.
        var firstLine = plain.LastPrompt!.Split('\n')[0];
        Assert.Equal(firstLine, asked.LastPrompt!.Split('\n')[0]);
        Assert.DoesNotContain("should be a type", asked.LastPrompt!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Type_relations_are_read_spelled_as_names_in_use_once_each_and_checked_one_by_one()
    {
        var proposal = await Propose(Answer, []);

        Assert.Collection(
            proposal.TypeRelations,
            t =>
            {
                Assert.Equal("HydraulicPress", t.SubType);
                Assert.Equal("Press", t.SuperType);
                Assert.Equal(ProposalBasis.Inferred, t.Basis);
                Assert.Equal("g-1", Assert.Single(t.Claim.Sources).RecordId);
            },
            t =>
            {
                Assert.Equal("Press", t.SubType);
                Assert.Equal("Equipment", t.SuperType);
            });
        Assert.Contains(proposal.Rejections, r => r is { Element: ProposalElement.TypeRelation, Reason: RejectionReason.ReflexiveTypeRelation, Id: "Conveyor" });
        Assert.Contains(proposal.Rejections, r => r is { Element: ProposalElement.TypeRelation, Reason: RejectionReason.MissingField } && r.Detail.Contains("cites no source", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_declared_super_type_asks_for_the_channel_shows_the_declaration_and_stamps_its_pair()
    {
        var model = new StubModelClient(Answer);

        var proposal = await Propose(Answer, [Declare("Press", "Equipment")], proposeTypeRelations: false, model: model);

        Assert.Contains("Declared type relation: Press is a kind of Equipment", model.LastPrompt!, StringComparison.Ordinal);
        Assert.Contains("typeRelations", model.LastPrompt!, StringComparison.Ordinal);
        var declared = Assert.Single(proposal.TypeRelations, t => t.SubType == "Press");
        Assert.Equal(ProposalBasis.Declared, declared.Basis);
        Assert.Equal(ProposalBasis.Inferred, Assert.Single(proposal.TypeRelations, t => t.SubType == "HydraulicPress").Basis);
    }

    [Fact]
    public async Task A_type_relation_that_inverts_a_declared_one_is_left_out()
    {
        var proposal = await Propose(Answer, [Declare("Equipment", "Press")]);

        Assert.DoesNotContain(proposal.TypeRelations, t => t.SubType == "Press" && t.SuperType == "Equipment");
        Assert.Contains(proposal.Rejections, r => r is { Element: ProposalElement.TypeRelation, Reason: RejectionReason.ContradictsDeclaration });
    }

    [Fact]
    public async Task A_type_relation_citing_a_record_the_call_was_not_given_refuses_the_answer()
    {
        const string answer = """{"entities": [], "relations": [], "typeRelations": [{"sub": "A", "super": "B", "claim": "x", "sources": ["nowhere"], "confidence": 0.5}]}""";

        await Assert.ThrowsAsync<FormatException>(() => Propose(answer, []));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("--")]
    [InlineData("press")]
    public async Task A_super_type_no_merge_could_honor_is_refused_before_the_model_is_called(string superType)
    {
        var model = new StubModelClient(Answer);

        await Assert.ThrowsAsync<ArgumentException>(() => Propose(Answer, [Declare("Press", superType)], model: model));
        Assert.Null(model.LastRequest);
    }

    [Fact]
    public void A_type_is_not_a_kind_of_itself_when_built_by_hand()
    {
        var claim = GroundedClaim.Create("x", [new SourceRef("g-1")]);

        Assert.Throws<ArgumentException>(() => TypeRelationProposal.Create("Work Order", "work_order", claim, 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => TypeRelationProposal.Create("A", "B", claim, 1.5));
    }

    [Fact]
    public void The_channel_has_a_fingerprint_of_its_own_and_leaves_the_others()
    {
        Assert.Matches("^[0-9a-f]{8}$", SinglePassOntologyProposer.TypeRelationsPromptFingerprint);
        Assert.Equal("d911c6a8", SinglePassOntologyProposer.KnownEntitiesPromptFingerprint);
        Assert.NotEqual(SinglePassOntologyProposer.PromptFingerprint, SinglePassOntologyProposer.TypeRelationsPromptFingerprint);
    }
}
