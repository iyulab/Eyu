using System.Text.Json.Nodes;
using Eyu.Core.Inference;
using Eyu.Core.Judgment;
using Eyu.Core.Linkage;
using Eyu.Core.Ports;
using Eyu.Core.Proposals;
using Eyu.Core.Records;
using Xunit;

namespace Eyu.Core.Tests.Judgment;

/// <summary>
/// Mentioned entities: what earlier calls saw only named — a work order naming its machine — handed back
/// so a call that meets a record of the thing itself can say the two may be one. The answer is a merge
/// candidate, never an identity: a mention claims nothing about what the entity is.
/// </summary>
public sealed class MentionedEntityProposalTests
{
    private static readonly RawRecord WorkOrder = new("cmms-wo-1", new Dictionary<string, string?> { ["equipment"] = "프레스#4", ["task"] = "유압 점검" });

    private static readonly MentionedEntity MentionedPress = new("key:cmms/press", "프레스#4", "Equipment", [WorkOrder]);

    private static readonly IReadOnlyList<RawRecord> Ledger =
    [
        new("erp-eq-1", new Dictionary<string, string?> { ["asset_code"] = "PRS-004", ["name"] = "프레스 4호기" }),
        new("erp-eq-2", new Dictionary<string, string?> { ["asset_code"] = "CNV-012", ["name"] = "컨베이어 12" }),
    ];

    private const string CandidateResponse = """
        {
          "entities": [
            {"id": "e1", "name": "프레스 4호기", "type": "Equipment", "claim": "erp-eq-1 is the press", "sources": ["erp-eq-1"], "denotedBy": ["erp-eq-1"], "confidence": 0.9},
            {"id": "e2", "name": "컨베이어 12", "type": "Equipment", "claim": "erp-eq-2 is the conveyor", "sources": ["erp-eq-2"], "denotedBy": ["erp-eq-2"], "confidence": 0.9},
            {"id": "e3", "name": "A라인", "type": "Line", "claim": "erp-eq-1 names a line", "sources": ["erp-eq-1"], "denotedBy": [], "confidence": 0.6}
          ],
          "relations": [],
          "mergeCandidates": [
            {"entity": "e1", "mentionedKey": "key:cmms/press", "claim": "PRS-004 is press no. 4, which the work order calls 프레스#4", "sources": ["erp-eq-1"], "confidence": 0.8}
          ]
        }
        """;

    private static Task<OntologyProposal> Propose(string response, IReadOnlyList<MentionedEntity> mentioned, StubModelClient? model = null) =>
        new SinglePassOntologyProposer(model ?? new StubModelClient(response)).ProposeAsync([], Ledger, [], mentioned, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Without_mentioned_entities_the_request_is_the_one_a_call_always_sent()
    {
        var plain = new StubModelClient(CandidateResponse);
        var withNone = new StubModelClient(CandidateResponse);

        await new SinglePassOntologyProposer(plain).ProposeAsync([], Ledger, TestContext.Current.CancellationToken);
        await Propose(CandidateResponse, [], withNone);

        Assert.Equal(plain.LastPrompt, withNone.LastPrompt);
        Assert.Equal(plain.LastRequest!.ResponseSchema!.Value.GetRawText(), withNone.LastRequest!.ResponseSchema!.Value.GetRawText());
        Assert.DoesNotContain("mergeCandidates", plain.LastPrompt!, StringComparison.Ordinal);
    }

    [Fact]
    public void The_fingerprints_measurements_were_taken_under_are_unchanged_and_the_new_one_is_its_own()
    {
        // Reports taken before merge candidates existed stamp these two; adding the feature must not move
        // them, or a run without mentioned entities would read as incomparable with every earlier one.
        Assert.Equal("2bc32b71", SinglePassOntologyProposer.PromptFingerprint);
        Assert.Equal("2bc58427", SinglePassOntologyProposer.KnownEntitiesPromptFingerprint);
        Assert.Matches("^[0-9a-f]{8}$", SinglePassOntologyProposer.MentionedEntitiesPromptFingerprint);
        Assert.NotEqual(SinglePassOntologyProposer.PromptFingerprint, SinglePassOntologyProposer.MentionedEntitiesPromptFingerprint);
        Assert.NotEqual(SinglePassOntologyProposer.KnownEntitiesPromptFingerprint, SinglePassOntologyProposer.MentionedEntitiesPromptFingerprint);
    }

    [Fact]
    public async Task Mentioned_entities_are_listed_with_their_records_before_the_records_and_the_schema_asks_for_candidates()
    {
        var model = new StubModelClient(CandidateResponse);

        await Propose(CandidateResponse, [MentionedPress], model);

        var prompt = model.LastPrompt!;
        Assert.Contains("Mentioned entity: key=key:cmms/press, type=Equipment, name=프레스#4", prompt, StringComparison.Ordinal);
        Assert.Contains("  - cmms-wo-1: equipment=프레스#4, task=유압 점검", prompt, StringComparison.Ordinal);
        Assert.Contains("is never a \"knownEntityKey\"", prompt, StringComparison.Ordinal);
        Assert.True(prompt.IndexOf("Mentioned entity:", StringComparison.Ordinal) < prompt.IndexOf("Records:", StringComparison.Ordinal));

        var schema = model.LastRequest!.ResponseSchema!.Value;
        Assert.Contains("mergeCandidates", schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        var item = schema.GetProperty("properties").GetProperty("mergeCandidates").GetProperty("items");
        Assert.Equal(["entity", "mentionedKey", "claim", "sources", "confidence"], item.GetProperty("properties").EnumerateObject().Select(p => p.Name));
        Assert.False(item.GetProperty("additionalProperties").GetBoolean());
        Assert.False(schema.GetProperty("properties").GetProperty("entities").GetProperty("items").GetProperty("properties").TryGetProperty("knownEntityKey", out _),
            "mentioned entities alone do not ask for a known key");
    }

    [Fact]
    public async Task The_prompt_sentence_names_the_same_candidate_fields_as_the_schema()
    {
        var model = new StubModelClient(CandidateResponse);

        await Propose(CandidateResponse, [MentionedPress], model);

        var sentence = model.LastPrompt!.Split(Environment.NewLine).Single(line => line.StartsWith("Also include in the same object", StringComparison.Ordinal));
        var schema = model.LastRequest!.ResponseSchema!.Value.GetProperty("properties").GetProperty("mergeCandidates").GetProperty("items");
        var named = System.Text.RegularExpressions.Regex.Matches(sentence[sentence.IndexOf('{', StringComparison.Ordinal)..], "\"(?<name>[^\"]+)\"").Select(m => m.Groups["name"].Value);
        Assert.Equal(schema.GetProperty("properties").EnumerateObject().Select(p => p.Name), named);
    }

    [Fact]
    public async Task A_candidate_built_from_the_schema_parses_without_rejections()
    {
        var capture = new StubModelClient(CandidateResponse);
        await Propose(CandidateResponse, [MentionedPress], capture);
        var item = capture.LastRequest!.ResponseSchema!.Value.GetProperty("properties").GetProperty("mergeCandidates").GetProperty("items");
        var candidate = new JsonObject();
        foreach (var property in item.GetProperty("properties").EnumerateObject())
        {
            candidate[property.Name] = property.Name switch
            {
                "entity" => "e1",
                "mentionedKey" => MentionedPress.Key,
                "sources" => new JsonArray("erp-eq-1"),
                "confidence" => 0.5,
                _ => "text",
            };
        }

        var response = JsonNode.Parse(CandidateResponse)!.AsObject();
        response["mergeCandidates"] = new JsonArray(candidate);

        var proposal = await Propose(response.ToJsonString(), [MentionedPress]);

        Assert.Empty(proposal.Rejections);
        Assert.Single(proposal.MergeCandidates);
    }

    [Fact]
    public async Task A_candidate_is_reported_and_is_not_an_identity()
    {
        var proposal = await Propose(CandidateResponse, [MentionedPress]);

        Assert.Empty(proposal.Rejections);
        var candidate = Assert.Single(proposal.MergeCandidates);
        Assert.Equal("e1", candidate.EntityId);
        Assert.Equal("key:cmms/press", candidate.MentionedEntityKey);
        Assert.Equal(0.8, candidate.Confidence);
        Assert.Equal(["erp-eq-1"], candidate.Claim.Sources.Select(s => s.RecordId));
        Assert.All(proposal.Entities, e => Assert.Null(e.KnownEntityKey));
    }

    public static TheoryData<string, string, string, RejectionReason> Defective => new()
    {
        { "a mentioned key the call did not supply", "\"mentionedKey\": \"key:cmms/press\"", "\"mentionedKey\": \"key:cmms/other\"", RejectionReason.UnknownMentionedEntity },
        { "an entity the response never proposed", "\"entity\": \"e1\"", "\"entity\": \"e9\"", RejectionReason.CandidateEntityUnresolved },
        { "an entity no record here denotes", "\"entity\": \"e1\"", "\"entity\": \"e3\"", RejectionReason.CandidateEntityNotDenoted },
        { "a confidence outside [0, 1]", "\"confidence\": 0.8}", "\"confidence\": 1.8}", RejectionReason.ConfidenceOutOfRange },
        { "no claim", "\"claim\": \"PRS-004 is press no. 4, which the work order calls 프레스#4\"", "\"claim\": \" \"", RejectionReason.MissingField },
    };

    [Theory]
    [MemberData(nameof(Defective))]
    public async Task A_defective_candidate_is_left_out_and_only_it(string because, string find, string replace, RejectionReason reason)
    {
        var response = CandidateResponse.Replace(find, replace, StringComparison.Ordinal);
        Assert.NotEqual(CandidateResponse, response);

        var proposal = await Propose(response, [MentionedPress]);

        Assert.Empty(proposal.MergeCandidates);
        var rejected = Assert.Single(proposal.Rejections);
        Assert.True(rejected.Element == ProposalElement.MergeCandidate && rejected.Reason == reason, $"{because}: {rejected}");
        Assert.Equal(3, proposal.Entities.Count);
    }

    [Fact]
    public async Task A_candidate_whose_entity_was_rejected_says_so()
    {
        var response = CandidateResponse.Replace("\"claim\": \"erp-eq-1 is the press\", \"sources\": [\"erp-eq-1\"], \"denotedBy\": [\"erp-eq-1\"], \"confidence\": 0.9", "\"claim\": \"erp-eq-1 is the press\", \"sources\": [\"erp-eq-1\"], \"denotedBy\": [\"erp-eq-1\"], \"confidence\": 7", StringComparison.Ordinal);
        Assert.NotEqual(CandidateResponse, response);

        var proposal = await Propose(response, [MentionedPress]);

        var rejected = Assert.Single(proposal.Rejections, r => r.Element == ProposalElement.MergeCandidate);
        Assert.Equal(RejectionReason.CandidateEntityUnresolved, rejected.Reason);
        Assert.Contains("was proposed but rejected", rejected.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Where_records_do_not_each_denote_an_entity_no_candidate_survives()
    {
        var model = new StubModelClient(CandidateResponse);
        var proposer = new SinglePassOntologyProposer(model, LinkageOptions.Default with { RecordsDenoteEntities = false });

        var proposal = await proposer.ProposeAsync([], Ledger, [], [MentionedPress], TestContext.Current.CancellationToken);

        Assert.Empty(proposal.MergeCandidates);
        Assert.Contains(proposal.Rejections, r => r.Reason == RejectionReason.CandidateEntityNotDenoted);
    }

    [Fact]
    public async Task The_same_pair_given_twice_is_one_candidate()
    {
        var node = JsonNode.Parse(CandidateResponse)!.AsObject();
        node["mergeCandidates"]!.AsArray().Add(node["mergeCandidates"]![0]!.DeepClone());

        var proposal = await Propose(node.ToJsonString(), [MentionedPress]);

        Assert.Single(proposal.MergeCandidates);
        Assert.Empty(proposal.Rejections);
    }

    [Fact]
    public async Task Citing_a_mentioned_entitys_record_refuses_the_whole_answer()
    {
        var response = CandidateResponse.Replace("\"sources\": [\"erp-eq-1\"], \"confidence\": 0.8", "\"sources\": [\"erp-eq-1\", \"cmms-wo-1\"], \"confidence\": 0.8", StringComparison.Ordinal);
        Assert.NotEqual(CandidateResponse, response);

        await Assert.ThrowsAsync<FormatException>(() => Propose(response, [MentionedPress]));
    }

    [Fact]
    public async Task A_candidate_answered_when_no_mentioned_entities_were_given_is_rejected()
    {
        var proposal = await new SinglePassOntologyProposer(new StubModelClient(CandidateResponse))
            .ProposeAsync([], Ledger, TestContext.Current.CancellationToken);

        Assert.Empty(proposal.MergeCandidates);
        Assert.Contains(proposal.Rejections, r => r.Reason == RejectionReason.UnknownMentionedEntity && r.Detail.Contains("supplied no mentioned entities", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Known_and_mentioned_entities_together_ask_for_both()
    {
        var known = new KnownEntity("key:erp/conveyor", "컨베이어 12", "Equipment", [new("erp-old-2", new Dictionary<string, string?> { ["asset_code"] = "CNV-012" })]);
        var model = new StubModelClient(CandidateResponse);

        var proposal = await new SinglePassOntologyProposer(model).ProposeAsync([], Ledger, [known], [MentionedPress], TestContext.Current.CancellationToken);

        var schema = model.LastRequest!.ResponseSchema!.Value;
        Assert.True(schema.GetProperty("properties").GetProperty("entities").GetProperty("items").GetProperty("properties").TryGetProperty("knownEntityKey", out _));
        Assert.True(schema.GetProperty("properties").TryGetProperty("mergeCandidates", out _));
        Assert.Single(proposal.MergeCandidates);
    }

    public static TheoryData<string, MentionedEntity[], KnownEntity[]> Contradictory => new()
    {
        { "a key given twice", [MentionedPress, MentionedPress with { Name = "other" }], [] },
        { "a blank type", [MentionedPress with { EntityType = "" }], [] },
        { "a key that also names a known entity", [MentionedPress], [new KnownEntity(MentionedPress.Key, "press", "Equipment", [WorkOrder])] },
        { "a mentioning record that is also a new one", [MentionedPress with { MentioningRecords = [Ledger[0]] }], [] },
    };

    [Theory]
    [MemberData(nameof(Contradictory))]
    public async Task Mentioned_entities_that_cannot_mean_one_thing_are_refused_before_the_model_is_asked(string because, MentionedEntity[] mentioned, KnownEntity[] known)
    {
        var model = new StubModelClient(CandidateResponse);

        await Assert.ThrowsAsync<ArgumentException>(() => new SinglePassOntologyProposer(model)
            .ProposeAsync([], Ledger, known, mentioned, TestContext.Current.CancellationToken));

        Assert.True(model.LastPrompt is null, because);
    }

    [Fact]
    public async Task A_mentioning_record_may_also_denote_a_known_entity()
    {
        // The work order denotes the repair and mentions the press: one row, two roles.
        var repair = new KnownEntity("key:cmms/repair-1", "유압 점검", "WorkOrder", [WorkOrder]);

        var proposal = await new SinglePassOntologyProposer(new StubModelClient(CandidateResponse))
            .ProposeAsync([], Ledger, [repair], [MentionedPress], TestContext.Current.CancellationToken);

        Assert.Single(proposal.MergeCandidates);
    }

    [Fact]
    public async Task The_port_forwards_the_known_entities_overload_with_no_mentioned_entities()
    {
        IOntologyProposer proposer = new SinglePassOntologyProposer(new StubModelClient(CandidateResponse));

        var proposal = await proposer.ProposeAsync([], Ledger, [], TestContext.Current.CancellationToken);

        Assert.Empty(proposal.MergeCandidates);
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
