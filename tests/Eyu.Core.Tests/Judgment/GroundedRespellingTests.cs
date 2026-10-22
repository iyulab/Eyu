using Eyu.Core.Judgment;
using Eyu.Core.Proposals;
using Eyu.Core.Records;
using Xunit;

namespace Eyu.Core.Tests.Judgment;

/// <summary>
/// A type name the answer misspelled is written as the name in use only when the records the answer cited
/// carry the name in use's words — nearness alone never respells a name.
/// </summary>
public sealed class GroundedRespellingTests
{
    private static readonly IReadOnlyList<RawRecord> Records =
    [
        new("r-1", new Dictionary<string, string?> { ["text"] = "Saithe is a fish; saithe food product is sold frozen." }),
        new("r-2", new Dictionary<string, string?> { ["text"] = "Fish food products include many kinds." }),
    ];

    private static readonly KnownType[] InUse = [new("saithe food product"), new("fish food product")];

    private static string Answer(string sub, string sources = "\"r-1\"", string entityType = "fish food product") => $$"""
        {
          "entities": [
            {"id": "e1", "name": "frozen saithe", "type": "{{entityType}}", "claim": "r-1 describes it", "sources": [{{sources}}], "denotedBy": [], "confidence": 0.7}
          ],
          "relations": [],
          "typeRelations": [
            {"sub": "{{sub}}", "super": "fish food product", "claim": "r-1 says so", "sources": [{{sources}}], "confidence": 0.9}
          ]
        }
        """;

    private static Task<OntologyProposal> Propose(string answer, params KnownType[] knownTypes) =>
        new SinglePassOntologyProposer(new StubModelClient(answer), proposeTypeRelations: true)
            .ProposeAsync([], Records, PriorKnowledge.Of(knownTypes), TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_misspelled_end_is_written_as_the_name_in_use_when_the_cited_record_carries_its_words()
    {
        var proposal = await Propose(Answer("saihe food product"), InUse);

        Assert.Equal("saithe food product", Assert.Single(proposal.TypeRelations).SubType);
        var respelled = Assert.Single(proposal.RespelledTypeNames);
        Assert.Equal(("saihe food product", "saithe food product"), (respelled.Answered, respelled.InUse));
        Assert.Equal(["r-1"], respelled.Sources);
    }

    [Fact]
    public async Task An_entity_type_is_respelled_the_same_way()
    {
        var proposal = await Propose(Answer("fish food product", entityType: "saithe food produt"), InUse);

        Assert.Equal("saithe food product", Assert.Single(proposal.Entities).EntityType);
        Assert.Equal("saithe food produt", Assert.Single(proposal.RespelledTypeNames).Answered);
    }

    /// <summary>
    /// An end respelled onto the other end's name makes the relation reflexive — rejected as one, like a
    /// relation answered reflexive. It used to throw from the relation's constructor and fail the whole call.
    /// </summary>
    [Fact]
    public async Task An_end_respelled_onto_the_other_ends_name_is_a_reflexive_relation_not_a_failed_call()
    {
        var proposal = await Propose(Answer("fish fod product"), InUse);

        Assert.Empty(proposal.TypeRelations);
        Assert.Contains(proposal.Rejections, r => r is { Element: ProposalElement.TypeRelation, Reason: RejectionReason.ReflexiveTypeRelation, Id: "fish fod product" });
        Assert.Single(proposal.Entities);
    }

    [Fact]
    public async Task A_near_name_the_cited_records_do_not_carry_is_left_as_answered()
    {
        // r-2 says nothing of saithe: the near name in use has no source here, so the answer's spelling stands.
        var proposal = await Propose(Answer("saihe food product", sources: "\"r-2\""), InUse);

        Assert.Equal("saihe food product", Assert.Single(proposal.TypeRelations).SubType);
        Assert.Empty(proposal.RespelledTypeNames);
    }

    [Fact]
    public async Task A_spelling_the_cited_records_carry_is_left_as_answered_even_beside_a_near_name_in_use()
    {
        // "Austria" is in the record and a letter or two from "Australia": two things, not a misspelling.
        IReadOnlyList<RawRecord> records = [new("a-1", new Dictionary<string, string?> { ["text"] = "Austria borders Germany; Australia does not." })];
        const string answer = """
            {"entities": [{"id": "e1", "name": "Vienna", "type": "Austria", "claim": "a-1", "sources": ["a-1"], "denotedBy": [], "confidence": 0.7}], "relations": []}
            """;

        var proposal = await new SinglePassOntologyProposer(new StubModelClient(answer))
            .ProposeAsync([], records, PriorKnowledge.Of([new KnownType("Australia")]), TestContext.Current.CancellationToken);

        Assert.Equal("Austria", Assert.Single(proposal.Entities).EntityType);
        Assert.Empty(proposal.RespelledTypeNames);
    }

    [Fact]
    public async Task Two_names_in_use_equally_near_leave_the_answer_as_written()
    {
        var proposal = await Propose(Answer("saihe food product"), [.. InUse, new KnownType("saiko food product")]);

        Assert.Equal("saihe food product", Assert.Single(proposal.TypeRelations).SubType);
        Assert.Empty(proposal.RespelledTypeNames);
    }

    [Fact]
    public async Task Without_names_in_use_nothing_is_respelled()
    {
        var proposal = await Propose(Answer("saihe food product"));

        Assert.Equal("saihe food product", Assert.Single(proposal.TypeRelations).SubType);
        Assert.Empty(proposal.RespelledTypeNames);
    }

    [Theory]
    [InlineData("saithe fd product", "saithe food product", true)]   // two letters dropped: within reach
    [InlineData("saithe f product", "saithe food product", false)]   // three: past it
    [InlineData("saihe", "saithe", false)]                           // under the letter floor (the record does say "saithe"): short names are near too many others
    public async Task The_edit_limit_and_the_letter_floor_bound_what_is_near(string answered, string inUse, bool respelled)
    {
        var proposal = await Propose(Answer(answered), new KnownType(inUse), new KnownType("fish food product"));

        Assert.Equal(respelled ? inUse : answered, Assert.Single(proposal.TypeRelations).SubType);
    }
}
