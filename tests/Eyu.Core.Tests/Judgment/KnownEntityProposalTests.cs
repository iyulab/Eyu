using Eyu.Core.Declared;
using Eyu.Core.Judgment;
using Eyu.Core.Ports;
using Eyu.Core.Inference;
using Eyu.Core.Primitives;
using Eyu.Core.Proposals;
using Eyu.Core.Records;
using Xunit;

namespace Eyu.Core.Tests.Judgment;

/// <summary>
/// Known entities: what earlier calls identified, handed back so a new call can say which of its
/// entities are the same things. Eyu keeps nothing between calls, so the list is input, and a match is
/// reported (<see cref="EntityProposal.KnownEntityKey"/>) rather than applied.
/// </summary>
public sealed class KnownEntityProposalTests
{
    private static readonly RawRecord ErpPress = new("erp-eq-1", new Dictionary<string, string?> { ["asset_code"] = "PRS-004", ["name"] = "프레스 4호기" });
    private static readonly RawRecord ErpConveyor = new("erp-eq-2", new Dictionary<string, string?> { ["asset_code"] = "CNV-012", ["name"] = "컨베이어 12" });

    private static readonly KnownEntity Press = new("https://example.org/entity/press-4", "프레스 4호기", "Equipment", [ErpPress]);
    private static readonly KnownEntity Conveyor = new("https://example.org/entity/conveyor-12", "컨베이어 12", "Equipment", [ErpConveyor]);

    private static readonly IReadOnlyList<RawRecord> WorkOrders =
    [
        new("cmms-wo-1", new Dictionary<string, string?> { ["equipment"] = "프레스#4", ["task"] = "유압 점검" }),
        new("cmms-wo-2", new Dictionary<string, string?> { ["equipment"] = "컨베이어#12", ["task"] = "벨트 교체" }),
    ];

    private const string MatchedResponse = """
        {
          "entities": [
            {"id": "e1", "name": "프레스#4", "type": "Equipment", "claim": "cmms-wo-1 names the press", "sources": ["cmms-wo-1"], "denotedBy": [], "knownEntityKey": "https://example.org/entity/press-4", "confidence": 0.8},
            {"id": "e2", "name": "유압 점검", "type": "WorkOrder", "claim": "cmms-wo-1 is a work order", "sources": ["cmms-wo-1"], "denotedBy": ["cmms-wo-1"], "knownEntityKey": null, "confidence": 0.9}
          ],
          "relations": [
            {"name": "services", "from": "e2", "to": "e1", "claim": "the work order is on the press", "sources": ["cmms-wo-1"], "confidence": 0.9}
          ]
        }
        """;

    [Fact]
    public async Task Without_known_entities_the_request_is_the_one_a_call_always_sent()
    {
        var plain = new StubModelClient(MatchedResponse);
        var withNone = new StubModelClient(MatchedResponse);

        await new SinglePassOntologyProposer(plain).ProposeAsync([], WorkOrders, TestContext.Current.CancellationToken);
        await new SinglePassOntologyProposer(withNone).ProposeAsync([], WorkOrders, [], TestContext.Current.CancellationToken);

        Assert.Equal(plain.LastPrompt, withNone.LastPrompt);
        Assert.Equal(plain.LastRequest!.ResponseSchema!.Value.GetRawText(), withNone.LastRequest!.ResponseSchema!.Value.GetRawText());
        Assert.DoesNotContain("knownEntityKey", plain.LastPrompt!, StringComparison.Ordinal);
        Assert.DoesNotContain("knownEntityKey", plain.LastRequest.ResponseSchema.Value.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Known_entities_are_listed_with_their_records_and_the_schema_asks_for_the_key()
    {
        var model = new StubModelClient(MatchedResponse);

        await new SinglePassOntologyProposer(model).ProposeAsync([], WorkOrders, [Press, Conveyor], TestContext.Current.CancellationToken);

        var prompt = model.LastPrompt!;
        Assert.Contains("Known entity: key=https://example.org/entity/press-4, type=Equipment, name=프레스 4호기", prompt, StringComparison.Ordinal);
        Assert.Contains("  - erp-eq-1: asset_code=PRS-004, name=프레스 4호기", prompt, StringComparison.Ordinal);
        Assert.Contains("cannot be cited as sources", prompt, StringComparison.Ordinal);
        Assert.True(prompt.IndexOf("Known entity:", StringComparison.Ordinal) < prompt.IndexOf("Records:", StringComparison.Ordinal),
            "the known entities come before the records they are compared with");

        var schema = model.LastRequest!.ResponseSchema!.Value;
        var entity = schema.GetProperty("properties").GetProperty("entities").GetProperty("items");
        Assert.True(entity.GetProperty("properties").TryGetProperty("knownEntityKey", out _));
        Assert.Contains("knownEntityKey", entity.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task A_match_carries_the_known_key_and_a_new_entity_carries_none()
    {
        var proposal = await new SinglePassOntologyProposer(new StubModelClient(MatchedResponse))
            .ProposeAsync([], WorkOrders, [Press, Conveyor], TestContext.Current.CancellationToken);

        Assert.Empty(proposal.Rejections);
        Assert.Equal("https://example.org/entity/press-4", proposal.Entities.Single(e => e.EntityId == "e1").KnownEntityKey);
        Assert.Null(proposal.Entities.Single(e => e.EntityId == "e2").KnownEntityKey);
        Assert.Equal("프레스#4", proposal.Entities.Single(e => e.EntityId == "e1").Name);
    }

    [Fact]
    public async Task A_key_the_call_did_not_supply_leaves_that_entity_out_and_only_it()
    {
        var response = MatchedResponse.Replace("https://example.org/entity/press-4", "https://example.org/entity/press-9", StringComparison.Ordinal);

        var proposal = await new SinglePassOntologyProposer(new StubModelClient(response))
            .ProposeAsync([], WorkOrders, [Press, Conveyor], TestContext.Current.CancellationToken);

        var rejected = Assert.Single(proposal.Rejections, r => r.Element == ProposalElement.Entity);
        Assert.Equal(RejectionReason.UnknownKnownEntity, rejected.Reason);
        Assert.Equal("e1", rejected.Id);
        Assert.Contains(proposal.Entities, e => e.EntityId == "e2");
        Assert.Contains(proposal.Rejections, r => r.Element == ProposalElement.Relation && r.Reason == RejectionReason.EndpointRejected);
    }

    [Fact]
    public async Task A_key_answered_when_no_known_entities_were_given_is_rejected_too()
    {
        var proposal = await new SinglePassOntologyProposer(new StubModelClient(MatchedResponse))
            .ProposeAsync([], WorkOrders, TestContext.Current.CancellationToken);

        Assert.Contains(proposal.Rejections, r => r.Id == "e1" && r.Reason == RejectionReason.UnknownKnownEntity);
    }

    [Fact]
    public async Task Citing_a_known_entitys_record_still_refuses_the_whole_answer()
    {
        var response = MatchedResponse.Replace("\"sources\": [\"cmms-wo-1\"], \"denotedBy\": [], \"knownEntityKey\": \"https", "\"sources\": [\"cmms-wo-1\", \"erp-eq-1\"], \"denotedBy\": [], \"knownEntityKey\": \"https", StringComparison.Ordinal);
        Assert.NotEqual(MatchedResponse, response);

        await Assert.ThrowsAsync<FormatException>(() => new SinglePassOntologyProposer(new StubModelClient(response))
            .ProposeAsync([], WorkOrders, [Press], TestContext.Current.CancellationToken));
    }

    public static TheoryData<string, KnownEntity[]> Contradictory => new()
    {
        { "a key given twice", [Press, Press with { Name = "other" }] },
        { "a record under two known entities", [Press, Conveyor with { DenotingRecords = [ErpPress] }] },
        { "a blank key", [Press with { Key = " " }] },
        { "a known record that is also a new one", [Press with { DenotingRecords = [WorkOrders[0]] }] },
    };

    [Theory]
    [MemberData(nameof(Contradictory))]
    public async Task Known_entities_that_cannot_mean_one_thing_are_refused_before_the_model_is_asked(string because, KnownEntity[] known)
    {
        var model = new StubModelClient(MatchedResponse);

        await Assert.ThrowsAsync<ArgumentException>(() => new SinglePassOntologyProposer(model)
            .ProposeAsync([], WorkOrders, known, TestContext.Current.CancellationToken));

        Assert.True(model.LastPrompt is null, because);
    }

    [Fact]
    public async Task A_declared_type_and_a_known_match_both_hold()
    {
        var declared = new DeclaredStructure(SubjectRef.Create("Equipment"), Fields: [], Relations: []);

        var proposal = await new SinglePassOntologyProposer(new StubModelClient(MatchedResponse))
            .ProposeAsync([declared], WorkOrders, [Press], TestContext.Current.CancellationToken);

        var press = proposal.Entities.Single(e => e.EntityId == "e1");
        Assert.Equal(ProposalBasis.Declared, press.Basis);
        Assert.Equal("https://example.org/entity/press-4", press.KnownEntityKey);
    }

    [Fact]
    public async Task The_port_forwards_a_call_without_known_entities_to_the_known_entities_overload()
    {
        IOntologyProposer proposer = new SinglePassOntologyProposer(new StubModelClient(MatchedResponse));

        var proposal = await proposer.ProposeAsync([], WorkOrders, TestContext.Current.CancellationToken);

        Assert.Contains(proposal.Entities, e => e.EntityId == "e2");
    }

    [Fact]
    public void The_known_entities_fingerprint_is_its_own()
    {
        Assert.Matches("^[0-9a-f]{8}$", SinglePassOntologyProposer.KnownEntitiesPromptFingerprint);
        Assert.NotEqual(SinglePassOntologyProposer.PromptFingerprint, SinglePassOntologyProposer.KnownEntitiesPromptFingerprint);
    }

    private sealed class StubModelClient(string responseText) : IModelClient
    {
        public string? LastPrompt { get; private set; }

        public ModelRequest? LastRequest { get; private set; }

        public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default)
        {
            LastPrompt = request.Prompt;
            LastRequest = request;
            return Task.FromResult(new ModelResponse(responseText));
        }
    }
}
