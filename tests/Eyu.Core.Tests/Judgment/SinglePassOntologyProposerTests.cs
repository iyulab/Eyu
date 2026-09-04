using Eyu.Core.Declared;
using Eyu.Core.Inference;
using Eyu.Core.Judgment;
using Eyu.Core.Linkage;
using Eyu.Core.Ports;
using Eyu.Core.Primitives;
using Eyu.Core.Proposals;
using Eyu.Core.Records;
using Xunit;

namespace Eyu.Core.Tests.Judgment;

public class SinglePassOntologyProposerTests
{
    [Fact]
    public async Task ProposeAsync_returns_an_empty_proposal_when_the_model_proposes_nothing()
    {
        var model = new StubModelClient("""{"entities":[],"relations":[]}""");
        var proposer = new SinglePassOntologyProposer(model);

        var proposal = await proposer.ProposeAsync(declaredStructure: null, records: []);

        Assert.Empty(proposal.Entities);
        Assert.Empty(proposal.Relations);
    }

    [Fact]
    public async Task ProposeAsync_parses_a_well_formed_entity_and_relation()
    {
        var model = new StubModelClient("""
            {
              "entities": [
                {"id": "e1", "type": "Person", "claim": "rec-1 denotes a person", "sources": ["rec-1"], "origin": "Innate", "confidence": 0.8}
              ],
              "relations": [
                {"name": "works_for", "from": "e1", "to": "e1", "claim": "rec-1 says so", "sources": ["rec-1"], "origin": "Acquired", "confidence": 0.4}
              ]
            }
            """);
        var proposer = new SinglePassOntologyProposer(model);

        var proposal = await proposer.ProposeAsync(declaredStructure: null, records: []);

        var entity = Assert.Single(proposal.Entities);
        Assert.Equal("e1", entity.EntityId);
        Assert.Equal("Person", entity.EntityType);
        Assert.Equal(VocabularyOrigin.Innate, entity.Origin);
        Assert.Equal(0.8, entity.Confidence);
        Assert.Equal("rec-1 denotes a person", entity.Claim.Claim);
        Assert.Equal("rec-1", Assert.Single(entity.Claim.Sources).RecordId);

        var relation = Assert.Single(proposal.Relations);
        Assert.Equal("works_for", relation.RelationName);
        Assert.Equal("e1", relation.FromEntityId);
        Assert.Equal(VocabularyOrigin.Acquired, relation.Origin);
    }

    [Fact]
    public async Task ProposeAsync_treats_a_missing_entities_or_relations_array_as_empty()
    {
        var model = new StubModelClient("{}");
        var proposer = new SinglePassOntologyProposer(model);

        var proposal = await proposer.ProposeAsync(declaredStructure: null, records: []);

        Assert.Empty(proposal.Entities);
        Assert.Empty(proposal.Relations);
    }

    [Fact]
    public async Task ProposeAsync_fails_loudly_when_the_model_response_is_not_valid_json()
    {
        var model = new StubModelClient("not json at all");
        var proposer = new SinglePassOntologyProposer(model);

        await Assert.ThrowsAsync<FormatException>(() => proposer.ProposeAsync(declaredStructure: null, records: []));
    }

    [Fact]
    public async Task ProposeAsync_does_not_swallow_an_out_of_range_confidence()
    {
        // The parser reuses EntityProposal.Create's own validation rather than re-implementing
        // it -- an invalid confidence from the model surfaces as the same error a hand-built
        // proposal would raise, not as silently-clamped or silently-dropped data.
        var model = new StubModelClient("""
            {"entities":[{"id":"e1","type":"Person","claim":"x","sources":["rec-1"],"origin":"Innate","confidence":1.5}],"relations":[]}
            """);
        var proposer = new SinglePassOntologyProposer(model);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => proposer.ProposeAsync(declaredStructure: null, records: []));
    }

    [Fact]
    public async Task ProposeAsync_builds_a_prompt_that_carries_the_declared_fields_and_record_contents()
    {
        var model = new StubModelClient("""{"entities":[],"relations":[]}""");
        var proposer = new SinglePassOntologyProposer(model);
        var structure = new DeclaredStructure(SubjectRef.Create("invoice"), Fields: [new DeclaredField("total")], Relations: []);
        var records = new[] { new RawRecord("rec-1", new Dictionary<string, string?> { ["total"] = "100" }) };

        await proposer.ProposeAsync(structure, records);

        Assert.Contains("total", model.LastPrompt);
        Assert.Contains("rec-1", model.LastPrompt);
        Assert.Contains("100", model.LastPrompt);
    }

    [Fact]
    public async Task ProposeAsync_tells_the_model_which_record_groups_are_already_linked()
    {
        var model = new StubModelClient("""{"entities":[],"relations":[]}""");
        var proposer = new SinglePassOntologyProposer(model);
        var records = new[]
        {
            new RawRecord("rec-1", new Dictionary<string, string?> { ["name"] = "Acme Corp", ["city"] = "Springfield" }),
            new RawRecord("rec-2", new Dictionary<string, string?> { ["name"] = "Acme Corp", ["city"] = "Springfield" }),
        };

        await proposer.ProposeAsync(declaredStructure: null, records);

        Assert.Contains("Pre-linked record groups", model.LastPrompt);
        Assert.Contains("rec-1", model.LastPrompt);
        Assert.Contains("rec-2", model.LastPrompt);
    }

    [Fact]
    public async Task ProposeAsync_hands_the_model_the_gray_zone_pairs_with_their_prior_log_odds()
    {
        var model = new StubModelClient("""{"entities":[],"relations":[]}""");
        var proposer = new SinglePassOntologyProposer(model);
        var records = new[]
        {
            new RawRecord("rec-1", new Dictionary<string, string?> { ["name"] = "Acme Corp", ["city"] = "Springfield" }),
            new RawRecord("rec-2", new Dictionary<string, string?> { ["name"] = "Acme Corp", ["city"] = "Portland" }),
        };

        await proposer.ProposeAsync(declaredStructure: null, records);

        Assert.Contains("Ambiguous record pairs needing your judgment", model.LastPrompt);
        Assert.Contains("prior log-odds", model.LastPrompt);
    }

    [Fact]
    public async Task ProposeAsync_overrides_confidence_with_FS_derived_probability_for_a_clear_match()
    {
        var model = new StubModelClient("""
            {"entities":[{"id":"e1","type":"Organization","claim":"rec-1 and rec-2 are the same org","sources":["rec-1","rec-2"],"origin":"Innate","confidence":0.5}],"relations":[]}
            """);
        var proposer = new SinglePassOntologyProposer(model);
        var records = new[]
        {
            new RawRecord("rec-1", new Dictionary<string, string?> { ["name"] = "Acme Corp", ["city"] = "Springfield" }),
            new RawRecord("rec-2", new Dictionary<string, string?> { ["name"] = "Acme Corp", ["city"] = "Springfield" }),
        };

        var proposal = await proposer.ProposeAsync(declaredStructure: null, records);

        var entity = Assert.Single(proposal.Entities);
        // Below the EM floor (2 records -> 1 pair), so the heuristic default m=0.9/u=0.1 applies.
        // Both fields agree: LLR = 2 * ln(0.9/0.1) -> Match -> confidence = sigmoid(LLR), which
        // must NOT equal the model's own (deliberately different) confidence of 0.5.
        var expectedLlr = 2 * Math.Log(0.9 / 0.1);
        var expectedConfidence = 1.0 / (1.0 + Math.Exp(-expectedLlr));
        Assert.Equal(expectedConfidence, entity.Confidence, precision: 6);
        Assert.NotEqual(0.5, entity.Confidence);
    }

    [Fact]
    public async Task ProposeAsync_combines_FS_prior_with_LLM_confidence_for_a_gray_zone_pair()
    {
        var model = new StubModelClient("""
            {"entities":[{"id":"e1","type":"Organization","claim":"rec-1 and rec-2 are the same org","sources":["rec-1","rec-2"],"origin":"Innate","confidence":0.9}],"relations":[]}
            """);
        var proposer = new SinglePassOntologyProposer(model);
        var records = new[]
        {
            new RawRecord("rec-1", new Dictionary<string, string?> { ["name"] = "Acme Corp", ["city"] = "Springfield" }),
            new RawRecord("rec-2", new Dictionary<string, string?> { ["name"] = "Acme Corp", ["city"] = "Portland" }),
        };

        var proposal = await proposer.ProposeAsync(declaredStructure: null, records);

        var entity = Assert.Single(proposal.Entities);
        // One field agrees, one disagrees -> heuristic LLR = ln(9) + ln(1/9) = 0 (a neutral
        // prior) -> GrayZone -> posterior = sigmoid(0 + logit(llmConfidence)) == llmConfidence.
        Assert.Equal(0.9, entity.Confidence, precision: 6);
    }

    [Fact]
    public async Task ProposeAsync_skips_linkage_for_a_single_record_batch()
    {
        var model = new StubModelClient("""
            {"entities":[{"id":"e1","type":"Person","claim":"rec-1 denotes a person","sources":["rec-1"],"origin":"Innate","confidence":0.7}],"relations":[]}
            """);
        var proposer = new SinglePassOntologyProposer(model);
        var records = new[] { new RawRecord("rec-1", new Dictionary<string, string?> { ["name"] = "Jane Doe" }) };

        var proposal = await proposer.ProposeAsync(declaredStructure: null, records);

        var entity = Assert.Single(proposal.Entities);
        Assert.Equal(0.7, entity.Confidence);
    }

    [Fact]
    public async Task ProposeAsync_honors_custom_LinkageOptions_thresholds()
    {
        var model = new StubModelClient("""{"entities":[],"relations":[]}""");
        var proposer = new SinglePassOntologyProposer(model, new LinkageOptions(MatchThreshold: 2.0));
        var records = new[]
        {
            new RawRecord("rec-1", new Dictionary<string, string?> { ["name"] = "Acme" }),
            new RawRecord("rec-2", new Dictionary<string, string?> { ["name"] = "Acme" }),
        };

        await proposer.ProposeAsync(declaredStructure: null, records);

        // A single shared agreeing field gives LLR = ln 9 ~ 2.197 -- GrayZone under the default
        // 4.0 threshold, but Match under this test's lowered 2.0 threshold, so the prompt must
        // carry the "Pre-linked record groups" hint instead of the "Ambiguous record pairs" one.
        Assert.Contains("Pre-linked record groups", model.LastPrompt);
        Assert.DoesNotContain("Ambiguous record pairs", model.LastPrompt);
    }

    private sealed class StubModelClient(string responseText) : IModelClient
    {
        public string? LastPrompt { get; private set; }

        public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default)
        {
            LastPrompt = request.Prompt;
            return Task.FromResult(new ModelResponse(responseText));
        }
    }
}
