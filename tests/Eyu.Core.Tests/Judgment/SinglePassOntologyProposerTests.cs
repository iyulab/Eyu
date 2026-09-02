using Eyu.Core.Declared;
using Eyu.Core.Inference;
using Eyu.Core.Judgment;
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
