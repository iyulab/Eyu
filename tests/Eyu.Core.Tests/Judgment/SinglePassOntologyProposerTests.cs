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

        var proposal = await proposer.ProposeAsync(declaredStructure: null, records: [OneRecord("rec-1")]);

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
    public async Task ProposeAsync_reports_what_the_model_actually_returned_when_the_json_is_invalid()
    {
        // A model that wraps its JSON in prose or a markdown fence fails the same way a truncated
        // or refused completion does. The text is already in hand at the throw site, so the caller
        // should not have to re-run with logging to tell those apart.
        var model = new StubModelClient("Sure! Here is the ontology: ```json {\"entities\":[]}```");
        var proposer = new SinglePassOntologyProposer(model);

        var error = await Assert.ThrowsAsync<FormatException>(
            () => proposer.ProposeAsync(declaredStructure: null, records: []));

        Assert.Contains("Sure! Here is the ontology", error.Message);
    }

    [Fact]
    public async Task ProposeAsync_bounds_the_reported_response_text()
    {
        var model = new StubModelClient(new string('x', 4000));
        var proposer = new SinglePassOntologyProposer(model);

        var error = await Assert.ThrowsAsync<FormatException>(
            () => proposer.ProposeAsync(declaredStructure: null, records: []));

        Assert.True(error.Message.Length < 1000, $"message was {error.Message.Length} chars");
        Assert.Contains("4000 chars total", error.Message);
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

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => proposer.ProposeAsync(declaredStructure: null, records: [OneRecord("rec-1")]));
    }

    [Fact]
    public async Task ProposeAsync_rejects_a_response_that_cites_a_record_it_was_never_given()
    {
        // The record ids are in hand at the call, so a cited id that is not among them needs no
        // model and no heuristic to detect -- it is the shape of grounding with none of the
        // substance, and "a claim that can't cite its sources cannot be expressed" has to mean
        // sources the call actually supplied.
        var model = new StubModelClient("""
            {"entities":[{"id":"e1","type":"Asset","claim":"x","sources":["ghost-record"],"origin":"Acquired","confidence":0.9}],"relations":[]}
            """);
        var proposer = new SinglePassOntologyProposer(model);

        var error = await Assert.ThrowsAsync<FormatException>(
            () => proposer.ProposeAsync(declaredStructure: null, records: [OneRecord("rec-1")]));

        Assert.Contains("ghost-record", error.Message);
        Assert.Contains("1 record(s)", error.Message);
    }

    [Fact]
    public async Task ProposeAsync_rejects_the_whole_response_when_one_citation_among_valid_ones_is_unknown()
    {
        var model = new StubModelClient("""
            {"entities":[
              {"id":"e1","type":"Asset","claim":"x","sources":["rec-1"],"origin":"Acquired","confidence":0.9},
              {"id":"e2","type":"Asset","claim":"y","sources":["rec-1","made-up"],"origin":"Acquired","confidence":0.9}
            ],"relations":[]}
            """);
        var proposer = new SinglePassOntologyProposer(model);

        var error = await Assert.ThrowsAsync<FormatException>(
            () => proposer.ProposeAsync(declaredStructure: null, records: [OneRecord("rec-1")]));

        Assert.Contains("made-up", error.Message);
        Assert.StartsWith("The model cited source id(s) it was never given: made-up", error.Message);
    }

    [Fact]
    public async Task ProposeAsync_checks_relation_citations_too()
    {
        var model = new StubModelClient("""
            {"entities":[{"id":"e1","type":"Asset","claim":"x","sources":["rec-1"],"origin":"Acquired","confidence":0.9}],
             "relations":[{"name":"r","from":"e1","to":"e1","claim":"z","sources":["nowhere"],"origin":"Acquired","confidence":0.5}]}
            """);
        var proposer = new SinglePassOntologyProposer(model);

        var error = await Assert.ThrowsAsync<FormatException>(
            () => proposer.ProposeAsync(declaredStructure: null, records: [OneRecord("rec-1")]));

        Assert.Contains("nowhere", error.Message);
    }

    [Fact]
    public async Task ProposeAsync_with_no_records_can_only_return_an_empty_proposal()
    {
        // A declared-only call has nothing citable. A model that cites anything anyway is refused
        // with a message that says why, so a consumer hitting this learns the rule rather than a
        // stray id.
        var model = new StubModelClient("""
            {"entities":[{"id":"e1","type":"Invoice","claim":"x","sources":["rec-1"],"origin":"Acquired","confidence":0.9}],"relations":[]}
            """);
        var proposer = new SinglePassOntologyProposer(model);
        var structure = new DeclaredStructure(SubjectRef.Create("invoice"), Fields: [new DeclaredField("total")], Relations: []);

        var error = await Assert.ThrowsAsync<FormatException>(
            () => proposer.ProposeAsync(structure, records: []));

        Assert.Contains("no records were supplied to this call", error.Message);
    }

    private static RawRecord OneRecord(string id) => new(id, new Dictionary<string, string?> { ["name"] = "x" });

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
    public async Task ProposeAsync_tells_the_model_every_declared_fact_and_that_declared_structure_is_authoritative()
    {
        // A declaration that reaches the prompt as a bare list of names has lost its type, its
        // required-ness and which field realises a relation -- the model then re-infers what the
        // caller already declared, which is the inference "Declared always wins" says must not win.
        var model = new StubModelClient("""{"entities":[],"relations":[]}""");
        var proposer = new SinglePassOntologyProposer(model);
        var structure = new DeclaredStructure(
            SubjectRef.Create("work_order"),
            Fields:
            [
                new DeclaredField("wo_no", Kind: DeclaredValueKind.Text, Required: true),
                new DeclaredField("qty", Kind: DeclaredValueKind.WholeNumber),
                new DeclaredField("total", SemanticHint: "monetary amount, minor units", Kind: DeclaredValueKind.FractionalNumber),
                new DeclaredField("note"),
            ],
            Relations:
            [
                new DeclaredRelation("asset", SubjectRef.Create("asset"), ViaField: "asset_tag", Kind: DeclaredRelationKind.Reference),
                new DeclaredRelation("owner", SubjectRef.Create("person")),
            ]);
        var records = new[] { new RawRecord("rec-1", new Dictionary<string, string?> { ["wo_no"] = "WO-1" }) };

        await proposer.ProposeAsync(structure, records);

        Assert.Contains("Declared structure is authoritative", model.LastPrompt);
        Assert.Contains("wo_no (text, required)", model.LastPrompt);
        Assert.Contains("qty (integer)", model.LastPrompt);
        Assert.Contains("total (decimal; monetary amount, minor units)", model.LastPrompt);
        Assert.Contains(", note", model.LastPrompt);
        Assert.Contains("Declared relation: asset -> asset (reference via asset_tag)", model.LastPrompt);
        Assert.Contains("Declared relation: owner -> person", model.LastPrompt);
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
