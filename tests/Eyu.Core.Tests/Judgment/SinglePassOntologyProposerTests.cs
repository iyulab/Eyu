using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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

        var proposal = await proposer.ProposeAsync(declaredStructures: [], records: [], TestContext.Current.CancellationToken);

        Assert.Empty(proposal.Entities);
        Assert.Empty(proposal.Relations);
    }

    [Fact]
    public async Task ProposeAsync_parses_a_well_formed_entity_and_relation()
    {
        var model = new StubModelClient("""
            {
              "entities": [
                {"id": "e1", "name": "e1-name", "type": "Person", "claim": "rec-1 denotes a person", "sources": ["rec-1"], "confidence": 0.8}
              ],
              "relations": [
                {"name": "works_for", "from": "e1", "to": "e1", "claim": "rec-1 says so", "sources": ["rec-1"], "confidence": 0.4}
              ]
            }
            """);
        var proposer = new SinglePassOntologyProposer(model);

        var proposal = await proposer.ProposeAsync(declaredStructures: [], records: [OneRecord("rec-1")], TestContext.Current.CancellationToken);

        var entity = Assert.Single(proposal.Entities);
        Assert.Equal("e1", entity.EntityId);
        Assert.Equal("e1-name", entity.Name);
        Assert.Equal("Person", entity.EntityType);
        Assert.Equal(VocabularyOrigin.Innate, entity.Origin);
        Assert.Equal(0.8, entity.Confidence);
        Assert.Equal("rec-1 denotes a person", entity.Claim.Claim);
        Assert.Equal("rec-1", Assert.Single(entity.Claim.Sources).RecordId);

        var relation = Assert.Single(proposal.Relations);
        Assert.Equal("works_for", relation.RelationName);
        Assert.Equal("e1", relation.FromEntityId);
        Assert.Equal(VocabularyOrigin.Acquired, relation.Origin);
        Assert.Empty(proposal.Rejections);
    }

    [Fact]
    public async Task ProposeAsync_treats_a_missing_entities_or_relations_array_as_empty()
    {
        var model = new StubModelClient("{}");
        var proposer = new SinglePassOntologyProposer(model);

        var proposal = await proposer.ProposeAsync(declaredStructures: [], records: [], TestContext.Current.CancellationToken);

        Assert.Empty(proposal.Entities);
        Assert.Empty(proposal.Relations);
    }

    [Fact]
    public async Task ProposeAsync_fails_loudly_when_the_model_response_is_not_valid_json()
    {
        var model = new StubModelClient("not json at all");
        var proposer = new SinglePassOntologyProposer(model);

        await Assert.ThrowsAsync<FormatException>(() => proposer.ProposeAsync(declaredStructures: [], records: [], TestContext.Current.CancellationToken));
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
            () => proposer.ProposeAsync(declaredStructures: [], records: [], TestContext.Current.CancellationToken));

        Assert.Contains("Sure! Here is the ontology", error.Message);
    }

    [Fact]
    public async Task ProposeAsync_bounds_the_reported_response_text()
    {
        var model = new StubModelClient(new string('x', 4000));
        var proposer = new SinglePassOntologyProposer(model);

        var error = await Assert.ThrowsAsync<FormatException>(
            () => proposer.ProposeAsync(declaredStructures: [], records: [], TestContext.Current.CancellationToken));

        Assert.True(error.Message.Length < 1000, $"message was {error.Message.Length} chars");
        Assert.Contains("4000 chars total", error.Message);
    }

    [Fact]
    public async Task ProposeAsync_does_not_swallow_an_out_of_range_confidence()
    {
        // An invalid confidence is neither clamped nor silently dropped: the entity is left out and
        // the rejection says why, so a caller measuring the model still sees it.
        var model = new StubModelClient("""
            {"entities":[{"id":"e1","name":"e1-name","type":"Person","claim":"x","sources":["rec-1"],"confidence":1.5}],"relations":[]}
            """);
        var proposer = new SinglePassOntologyProposer(model);

        var proposal = await proposer.ProposeAsync(declaredStructures: [], records: [OneRecord("rec-1")], TestContext.Current.CancellationToken);

        Assert.Empty(proposal.Entities);
        var rejection = Assert.Single(proposal.Rejections);
        Assert.Equal(RejectionReason.ConfidenceOutOfRange, rejection.Reason);
        Assert.Equal("e1", rejection.Id);
        Assert.Contains("1.5", rejection.Detail);
    }

    [Fact]
    public async Task ProposeAsync_rejects_a_response_that_cites_a_record_it_was_never_given()
    {
        // The record ids are in hand at the call, so a cited id that is not among them needs no
        // model and no heuristic to detect -- it is the shape of grounding with none of the
        // substance, and "a claim that can't cite its sources cannot be expressed" has to mean
        // sources the call actually supplied.
        var model = new StubModelClient("""
            {"entities":[{"id":"e1","name":"e1-name","type":"Asset","claim":"x","sources":["ghost-record"],"confidence":0.9}],"relations":[]}
            """);
        var proposer = new SinglePassOntologyProposer(model);

        var error = await Assert.ThrowsAsync<FormatException>(
            () => proposer.ProposeAsync(declaredStructures: [], records: [OneRecord("rec-1")], TestContext.Current.CancellationToken));

        Assert.Contains("ghost-record", error.Message);
        Assert.Contains("1 record(s)", error.Message);
    }

    [Fact]
    public async Task ProposeAsync_rejects_the_whole_response_when_one_citation_among_valid_ones_is_unknown()
    {
        var model = new StubModelClient("""
            {"entities":[
              {"id":"e1","name":"e1-name","type":"Asset","claim":"x","sources":["rec-1"],"confidence":0.9},
              {"id":"e2","name":"e2-name","type":"Asset","claim":"y","sources":["rec-1","made-up"],"confidence":0.9}
            ],"relations":[]}
            """);
        var proposer = new SinglePassOntologyProposer(model);

        var error = await Assert.ThrowsAsync<FormatException>(
            () => proposer.ProposeAsync(declaredStructures: [], records: [OneRecord("rec-1")], TestContext.Current.CancellationToken));

        Assert.Contains("made-up", error.Message);
        Assert.StartsWith("The model cited source id(s) it was never given: made-up", error.Message);
    }

    [Fact]
    public async Task ProposeAsync_checks_relation_citations_too()
    {
        var model = new StubModelClient("""
            {"entities":[{"id":"e1","name":"e1-name","type":"Asset","claim":"x","sources":["rec-1"],"confidence":0.9}],
             "relations":[{"name":"r","from":"e1","to":"e1","claim":"z","sources":["nowhere"],"confidence":0.5}]}
            """);
        var proposer = new SinglePassOntologyProposer(model);

        var error = await Assert.ThrowsAsync<FormatException>(
            () => proposer.ProposeAsync(declaredStructures: [], records: [OneRecord("rec-1")], TestContext.Current.CancellationToken));

        Assert.Contains("nowhere", error.Message);
    }

    [Fact]
    public async Task ProposeAsync_leaves_out_a_relation_whose_end_names_an_entity_the_response_never_proposed()
    {
        // The ids are all in the one document, so a relation to an entity that is not there is a
        // deterministic defect, not a judgment: nobody downstream could resolve that end. It costs
        // that relation only -- the entity and every other relation are grounded independently.
        var model = new StubModelClient("""
            {"entities":[{"id":"e1","name":"e1-name","type":"Asset","claim":"x","sources":["rec-1"],"confidence":0.9}],
             "relations":[
               {"name":"r","from":"e1","to":"e9","claim":"z","sources":["rec-1"],"confidence":0.5},
               {"name":"s","from":"e7","to":"e1","claim":"w","sources":["rec-1"],"confidence":0.5}]}
            """);
        var proposer = new SinglePassOntologyProposer(model);

        var proposal = await proposer.ProposeAsync(declaredStructures: [], records: [OneRecord("rec-1")], TestContext.Current.CancellationToken);

        Assert.Single(proposal.Entities);
        Assert.Empty(proposal.Relations);
        Assert.Collection(proposal.Rejections,
            r => { Assert.Equal(RejectionReason.DanglingRelationEnd, r.Reason); Assert.Equal("r", r.Id); Assert.Contains("e9", r.Detail); },
            r => { Assert.Equal(RejectionReason.DanglingRelationEnd, r.Reason); Assert.Equal("s", r.Id); Assert.Contains("e7", r.Detail); });
    }

    [Fact]
    public async Task ProposeAsync_leaves_out_every_entity_under_a_duplicated_id()
    {
        // Two entities under one id make every relation to that id ambiguous, and nothing says which
        // of the two the model meant -- picking one would be a guess, so both are left out.
        var model = new StubModelClient("""
            {"entities":[
               {"id":"e1","name":"e1-name","type":"Asset","claim":"x","sources":["rec-1"],"confidence":0.9},
               {"id":"e1","name":"e1-name","type":"Site","claim":"y","sources":["rec-1"],"confidence":0.9}],
             "relations":[]}
            """);
        var proposer = new SinglePassOntologyProposer(model);

        var proposal = await proposer.ProposeAsync(declaredStructures: [], records: [OneRecord("rec-1")], TestContext.Current.CancellationToken);

        Assert.Empty(proposal.Entities);
        Assert.Equal(2, proposal.Rejections.Count);
        Assert.All(proposal.Rejections, r => Assert.Equal((ProposalElement.Entity, "e1", RejectionReason.DuplicateEntityId), (r.Element, r.Id, r.Reason)));
    }

    [Fact]
    public async Task ProposeAsync_accepts_a_relation_whose_both_ends_are_proposed_entities()
    {
        var model = new StubModelClient("""
            {"entities":[
               {"id":"e1","name":"e1-name","type":"Asset","claim":"x","sources":["rec-1"],"confidence":0.9},
               {"id":"e2","name":"e2-name","type":"Site","claim":"y","sources":["rec-1"],"confidence":0.9}],
             "relations":[{"name":"located_at","from":"e1","to":"e2","claim":"z","sources":["rec-1"],"confidence":0.5}]}
            """);
        var proposer = new SinglePassOntologyProposer(model);

        var proposal = await proposer.ProposeAsync(declaredStructures: [], records: [OneRecord("rec-1")], TestContext.Current.CancellationToken);

        var relation = Assert.Single(proposal.Relations);
        Assert.Equal("e1", relation.FromEntityId);
        Assert.Equal("e2", relation.ToEntityId);
    }

    [Fact]
    public async Task ProposeAsync_with_no_records_can_only_return_an_empty_proposal()
    {
        // A declared-only call has nothing citable. A model that cites anything anyway is refused
        // with a message that says why, so a consumer hitting this learns the rule rather than a
        // stray id.
        var model = new StubModelClient("""
            {"entities":[{"id":"e1","name":"e1-name","type":"Invoice","claim":"x","sources":["rec-1"],"confidence":0.9}],"relations":[]}
            """);
        var proposer = new SinglePassOntologyProposer(model);
        var structure = new DeclaredStructure(SubjectRef.Create("invoice"), Fields: [new DeclaredField("total")], Relations: []);

        var error = await Assert.ThrowsAsync<FormatException>(
            () => proposer.ProposeAsync([structure], records: [], TestContext.Current.CancellationToken));

        Assert.Contains("no records were supplied to this call", error.Message);
    }

    [Fact]
    public async Task ProposeAsync_stamps_origin_from_the_innate_vocabulary_whatever_the_model_says()
    {
        // A model told only that origin is "Innate" or "Acquired" cannot know which is which -- a
        // dogfooding run returned Acquired for Person and Organization alike. The model's own
        // "origin" field is ignored; the type or relation name decides.
        var model = new StubModelClient("""
            {"entities":[
               {"id":"e1","name":"Kim","type":"person","claim":"x","sources":["rec-1"],"origin":"Acquired","confidence":0.9},
               {"id":"e2","name":"Acme","type":"Company","claim":"y","sources":["rec-1"],"origin":"Innate","confidence":0.9}],
             "relations":[
               {"name":"part_of","from":"e1","to":"e2","claim":"z","sources":["rec-1"],"origin":"Acquired","confidence":0.5},
               {"name":"employs","from":"e2","to":"e1","claim":"w","sources":["rec-1"],"origin":"Innate","confidence":0.5}]}
            """);
        var proposer = new SinglePassOntologyProposer(model);

        var proposal = await proposer.ProposeAsync(declaredStructures: [], records: [OneRecord("rec-1")], TestContext.Current.CancellationToken);

        Assert.Equal([VocabularyOrigin.Innate, VocabularyOrigin.Acquired], proposal.Entities.Select(e => e.Origin));
        Assert.Equal([VocabularyOrigin.Innate, VocabularyOrigin.Acquired], proposal.Relations.Select(r => r.Origin));
    }

    [Fact]
    public async Task ProposeAsync_leaves_out_an_entity_without_a_name_and_every_relation_that_depends_on_it()
    {
        // Where records do not each denote one entity, the name is the only thing that identifies
        // the entity outside this call; an entity without one cannot be carried. A relation to it
        // is reported as depending on a rejected entity, which is a different finding from an end
        // the model never proposed at all.
        var model = new StubModelClient("""
            {"entities":[
               {"id":"E1","name":"한빛테크","type":"Organization","claim":"x","sources":["rec-1"],"confidence":1.0},
               {"id":"E2","name":"  ","type":"Person","claim":"y","sources":["rec-1"],"confidence":1.0}],
             "relations":[{"name":"CEO","from":"E2","to":"E1","claim":"z","sources":["rec-1"],"confidence":1.0}]}
            """);
        var proposer = new SinglePassOntologyProposer(model);

        var proposal = await proposer.ProposeAsync(declaredStructures: [], records: [OneRecord("rec-1")], TestContext.Current.CancellationToken);

        Assert.Equal("한빛테크", Assert.Single(proposal.Entities).Name);
        Assert.Empty(proposal.Relations);
        Assert.Collection(proposal.Rejections,
            r => Assert.Equal((ProposalElement.Entity, "E2", RejectionReason.MissingField), (r.Element, r.Id, r.Reason)),
            r => Assert.Equal((ProposalElement.Relation, "CEO", RejectionReason.EndpointRejected), (r.Element, r.Id, r.Reason)));
        Assert.Contains("\"name\"", proposal.Rejections[0].Detail);
    }

    [Theory]
    [InlineData("""{"id":"e1","name":"n","type":"T","claim":"x","confidence":0.5}""", "cites no source")]
    [InlineData("""{"id":"e1","name":"n","type":"T","claim":"x","sources":[],"confidence":0.5}""", "cites no source")]
    [InlineData("""{"id":"e1","name":"n","claim":"x","sources":["rec-1"],"confidence":0.5}""", "\"type\"")]
    [InlineData("""{"name":"n","type":"T","claim":"x","sources":["rec-1"],"confidence":0.5}""", "\"id\"")]
    [InlineData("""{"id":"e1","name":"n","type":"T","sources":["rec-1"],"confidence":0.5}""", "\"claim\"")]
    [InlineData("""{"id":"e1","name":"n","type":"T","claim":"x","sources":["rec-1"]}""", "\"confidence\"")]
    public async Task ProposeAsync_reports_an_incomplete_entity_as_a_missing_field_rather_than_throwing(string entity, string expectedDetail)
    {
        // Each of these used to escape as a different exception type (ArgumentException,
        // ArgumentNullException from inside LINQ), none of which a caller could classify.
        var model = new StubModelClient($$"""{"entities":[{{entity}}],"relations":[]}""");
        var proposer = new SinglePassOntologyProposer(model);

        var proposal = await proposer.ProposeAsync(declaredStructures: [], records: [OneRecord("rec-1")], TestContext.Current.CancellationToken);

        Assert.Empty(proposal.Entities);
        var rejection = Assert.Single(proposal.Rejections);
        Assert.Equal(RejectionReason.MissingField, rejection.Reason);
        Assert.Contains(expectedDetail, rejection.Detail);
    }

    [Fact]
    public async Task ProposeAsync_keeps_the_rest_of_a_batch_when_one_relation_points_at_a_value_literal()
    {
        // The shape a multi-chunk batch produced in practice: one relation whose ends are a year and
        // a place the model never stood up as entities. Before, that one line cost the whole batch.
        var model = new StubModelClient("""
            {"entities":[
               {"id":"E1","name":"Acme","type":"Organization","claim":"x","sources":["c1"],"confidence":1.0},
               {"id":"E2","name":"Kim","type":"Person","claim":"y","sources":["c2"],"confidence":1.0}],
             "relations":[
               {"name":"CEO","from":"E2","to":"E1","claim":"z","sources":["c2"],"confidence":1.0},
               {"name":"FoundedIn","from":"2012","to":"대전","claim":"w","sources":["c1"],"confidence":1.0}]}
            """);
        var proposer = new SinglePassOntologyProposer(model, new LinkageOptions(RecordsDenoteEntities: false));

        var proposal = await proposer.ProposeAsync(declaredStructures: [], records: [OneRecord("c1"), OneRecord("c2")], TestContext.Current.CancellationToken);

        Assert.Equal(2, proposal.Entities.Count);
        Assert.Equal("CEO", Assert.Single(proposal.Relations).RelationName);
        var rejection = Assert.Single(proposal.Rejections);
        Assert.Equal(RejectionReason.DanglingRelationEnd, rejection.Reason);
        Assert.Contains("2012, 대전", rejection.Detail);
    }

    [Fact]
    public async Task ProposeAsync_still_refuses_the_whole_response_when_an_element_cites_an_invented_source()
    {
        // Every other defect costs only its element. An invented citation does not: the check can
        // see that an id exists, never that the record says what the claim says, so a response
        // that fabricated evidence once gives no ground for trusting its other citations.
        var model = new StubModelClient("""
            {"entities":[
               {"id":"e1","name":"a","type":"T","claim":"x","sources":["rec-1"],"confidence":0.9},
               {"id":"e2","type":"T","claim":"y","sources":["made-up"],"confidence":0.9}],
             "relations":[]}
            """);
        var proposer = new SinglePassOntologyProposer(model);

        var error = await Assert.ThrowsAsync<FormatException>(
            () => proposer.ProposeAsync(declaredStructures: [], records: [OneRecord("rec-1")], TestContext.Current.CancellationToken));

        Assert.StartsWith("The model cited source id(s) it was never given: made-up", error.Message);
    }

    private static RawRecord OneRecord(string id) => new(id, new Dictionary<string, string?> { ["name"] = "x" });

    [Fact]
    public async Task ProposeAsync_builds_a_prompt_that_carries_the_declared_fields_and_record_contents()
    {
        var model = new StubModelClient("""{"entities":[],"relations":[]}""");
        var proposer = new SinglePassOntologyProposer(model);
        var structure = new DeclaredStructure(SubjectRef.Create("invoice"), Fields: [new DeclaredField("total")], Relations: []);
        var records = new[] { new RawRecord("rec-1", new Dictionary<string, string?> { ["total"] = "100" }) };

        await proposer.ProposeAsync([structure], records, TestContext.Current.CancellationToken);

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

        await proposer.ProposeAsync([structure], records, TestContext.Current.CancellationToken);

        Assert.Contains("Declared structure is authoritative", model.LastPrompt);
        Assert.Contains("wo_no (text, required)", model.LastPrompt);
        Assert.Contains("qty (integer)", model.LastPrompt);
        Assert.Contains("total (decimal; monetary amount, minor units)", model.LastPrompt);
        Assert.Contains(", note", model.LastPrompt);
        Assert.Contains("Declared type: work_order (fields: wo_no (text, required), qty (integer)", model.LastPrompt);
        Assert.Contains("Declared relation: asset: work_order -> asset (reference via asset_tag)", model.LastPrompt);
        Assert.Contains("Declared relation: owner: work_order -> person", model.LastPrompt);
    }

    [Fact]
    public async Task ProposeAsync_tells_the_model_every_declared_type_including_types_declared_by_name_alone()
    {
        // A document names many kinds of thing, and a caller that knows the vocabulary its records
        // use may know only the names. Each relation line carries the subject it leaves: with
        // several declared types, the name and target alone no longer say where a relation starts.
        var model = new StubModelClient("""{"entities":[],"relations":[]}""");
        var proposer = new SinglePassOntologyProposer(model);
        DeclaredStructure[] vocabulary =
        [
            new(SubjectRef.Create("Organization"), Fields: [], Relations: [new DeclaredRelation("PartnerOf", SubjectRef.Create("Organization"))]),
            new(SubjectRef.Create("Person"), Fields: [], Relations: [new DeclaredRelation("EmployedBy", SubjectRef.Create("Organization"))]),
            new(SubjectRef.Create("Product"), Fields: [], Relations: []),
        ];

        await proposer.ProposeAsync(vocabulary, records: [OneRecord("rec-1")], TestContext.Current.CancellationToken);

        var expected = string.Join(Environment.NewLine,
        [
            "Declared type: Organization",
            "Declared type: Person",
            "Declared type: Product",
            "Declared relation: PartnerOf: Organization -> Organization",
            "Declared relation: EmployedBy: Person -> Organization",
        ]);
        Assert.Contains(expected, model.LastPrompt);
    }

    [Fact]
    public async Task ProposeAsync_with_no_declarations_sends_no_declaration_clause()
    {
        var model = new StubModelClient("""{"entities":[],"relations":[]}""");
        var proposer = new SinglePassOntologyProposer(model);

        await proposer.ProposeAsync(declaredStructures: [], records: [OneRecord("rec-1")], TestContext.Current.CancellationToken);

        Assert.DoesNotContain("Declared", model.LastPrompt);
    }

    [Fact]
    public async Task ProposeAsync_sends_no_linkage_sections_when_records_do_not_denote_entities()
    {
        // Six chunks of one document: with the pre-filter on, every pair is a gray-zone line the
        // model is asked to adjudicate (fifteen of them here). With RecordsDenoteEntities off the
        // prompt carries the records and nothing about linking them.
        var model = new StubModelClient("""{"entities":[],"relations":[]}""");
        var proposer = new SinglePassOntologyProposer(model, new LinkageOptions(RecordsDenoteEntities: false));
        var records = Enumerable.Range(1, 6)
            .Select(i => new RawRecord($"chunk-{i}", new Dictionary<string, string?>
            {
                ["content"] = $"Paragraph {i} of the same document.",
                ["title"] = "Company profile",
                ["path"] = "/docs/profile.pptx",
            }))
            .ToList();

        await proposer.ProposeAsync(declaredStructures: [], records, TestContext.Current.CancellationToken);

        Assert.Contains("- chunk-6: content=Paragraph 6 of the same document.", model.LastPrompt);
        Assert.DoesNotContain("Pre-linked record groups", model.LastPrompt);
        Assert.DoesNotContain("Ambiguous record pairs", model.LastPrompt);
    }

    [Fact]
    public async Task ProposeAsync_declaration_clause_asks_for_inference_beyond_the_declaration()
    {
        // The declared-completeness ablation measured that, asked to "infer only what nothing
        // declares", the model read a partial declaration as the whole answer and inferred nothing
        // beyond it -- so declaring a third of a form reached fewer competency questions than
        // declaring none. The clause now names the declaration a floor, not a ceiling. This test
        // pins that wording, and pins the old clause's absence, so the behaviour the ablation
        // measures is the behaviour the prompt actually asks for; changing the sentence is a
        // design decision, and the failure is the reminder.
        var model = new StubModelClient("""{"entities":[],"relations":[]}""");
        var proposer = new SinglePassOntologyProposer(model);
        var structure = new DeclaredStructure(
            SubjectRef.Create("work_order"),
            Fields: [new DeclaredField("wo_no", Kind: DeclaredValueKind.Text, Required: true)],
            Relations: []);
        var records = new[] { new RawRecord("rec-1", new Dictionary<string, string?> { ["wo_no"] = "WO-1" }) };

        await proposer.ProposeAsync([structure], records, TestContext.Current.CancellationToken);

        // A declared type is vocabulary the model is asked to use: without this sentence the same
        // company came back as "Organization" in one document and "Company" in the next.
        Assert.Contains("Where a declared type describes an entity, propose the entity under that type; where a declared relation describes a relation, propose it under that name", model.LastPrompt);
        // And the declared types are not a closed list: asked only to go "beyond what is declared",
        // the model kept to exactly the declared entity types and dropped every other kind.
        Assert.Contains("A declaration is a floor, not a ceiling: the declared types and relations are not the only ones, so still propose every entity and relation the records show, under a type or name of your own wherever nothing declared describes it.", model.LastPrompt);
        Assert.DoesNotContain("infer only what nothing declares", model.LastPrompt);
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

        await proposer.ProposeAsync(declaredStructures: [], records, TestContext.Current.CancellationToken);

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

        await proposer.ProposeAsync(declaredStructures: [], records, TestContext.Current.CancellationToken);

        Assert.Contains("Ambiguous record pairs needing your judgment", model.LastPrompt);
        Assert.Contains("prior log-odds", model.LastPrompt);
    }

    [Fact]
    public async Task ProposeAsync_overrides_confidence_with_FS_derived_probability_for_a_clear_match()
    {
        var model = new StubModelClient("""
            {"entities":[{"id":"e1","name":"e1-name","type":"Organization","claim":"rec-1 and rec-2 are the same org","sources":["rec-1","rec-2"],"confidence":0.5}],"relations":[]}
            """);
        var proposer = new SinglePassOntologyProposer(model);
        var records = new[]
        {
            new RawRecord("rec-1", new Dictionary<string, string?> { ["name"] = "Acme Corp", ["city"] = "Springfield" }),
            new RawRecord("rec-2", new Dictionary<string, string?> { ["name"] = "Acme Corp", ["city"] = "Springfield" }),
        };

        var proposal = await proposer.ProposeAsync(declaredStructures: [], records, TestContext.Current.CancellationToken);

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
            {"entities":[{"id":"e1","name":"e1-name","type":"Organization","claim":"rec-1 and rec-2 are the same org","sources":["rec-1","rec-2"],"confidence":0.9}],"relations":[]}
            """);
        var proposer = new SinglePassOntologyProposer(model);
        var records = new[]
        {
            new RawRecord("rec-1", new Dictionary<string, string?> { ["name"] = "Acme Corp", ["city"] = "Springfield" }),
            new RawRecord("rec-2", new Dictionary<string, string?> { ["name"] = "Acme Corp", ["city"] = "Portland" }),
        };

        var proposal = await proposer.ProposeAsync(declaredStructures: [], records, TestContext.Current.CancellationToken);

        var entity = Assert.Single(proposal.Entities);
        // One field agrees, one disagrees -> heuristic LLR = ln(9) + ln(1/9) = 0 (a neutral
        // prior) -> GrayZone -> posterior = sigmoid(0 + logit(llmConfidence)) == llmConfidence.
        Assert.Equal(0.9, entity.Confidence, precision: 6);
    }

    [Fact]
    public async Task ProposeAsync_skips_linkage_for_a_single_record_batch()
    {
        var model = new StubModelClient("""
            {"entities":[{"id":"e1","name":"e1-name","type":"Person","claim":"rec-1 denotes a person","sources":["rec-1"],"confidence":0.7}],"relations":[]}
            """);
        var proposer = new SinglePassOntologyProposer(model);
        var records = new[] { new RawRecord("rec-1", new Dictionary<string, string?> { ["name"] = "Jane Doe" }) };

        var proposal = await proposer.ProposeAsync(declaredStructures: [], records, TestContext.Current.CancellationToken);

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

        await proposer.ProposeAsync(declaredStructures: [], records, TestContext.Current.CancellationToken);

        // A single shared agreeing field gives LLR = ln 9 ~ 2.197 -- GrayZone under the default
        // 4.0 threshold, but Match under this test's lowered 2.0 threshold, so the prompt must
        // carry the "Pre-linked record groups" hint instead of the "Ambiguous record pairs" one.
        Assert.Contains("Pre-linked record groups", model.LastPrompt);
        Assert.DoesNotContain("Ambiguous record pairs", model.LastPrompt);
    }

    [Fact]
    public void PromptFingerprint_is_eight_lowercase_hex_characters()
    {
        // A short stable identifier for the prompt's fixed text, stamped on measurement reports so
        // two runs made across a prompt edit are not read as comparable. The wording itself is
        // pinned by the preamble and declaration-clause tests; this only pins the identifier's shape
        // so a broken implementation (wrong length, uppercase, empty) is caught.
        Assert.Matches("^[0-9a-f]{8}$", SinglePassOntologyProposer.PromptFingerprint);
    }

    [Fact]
    public async Task ProposeAsync_prompt_preamble_does_not_say_what_counts_as_an_entity()
    {
        // The preamble is deliberately silent on what an entity is and on which kinds should
        // become types rather than instances. That question belongs to the innate grammar
        // (docs/philosophy.md, §C and §E), whose closed vocabulary classifies output after the
        // fact and is never shown to the model; a sentence steering it here would be that decision
        // made in the wrong place, and it would turn the competency-question harness from a
        // measurement of the model's modelling choices into a target the prompt is tuned against. This test pins the preamble so the sentence cannot arrive by accident --
        // changing it is a design decision, and the failure is the reminder.
        var model = new StubModelClient("""{"entities":[],"relations":[]}""");
        var proposer = new SinglePassOntologyProposer(model);

        await proposer.ProposeAsync(declaredStructures: [], records: [OneRecord("rec-1")], TestContext.Current.CancellationToken);

        var expectedPreamble = string.Join(Environment.NewLine,
        [
            "Propose entities and relations grounded in the input below.",
            "Respond with JSON only: {\"entities\":[{\"id\",\"name\",\"type\",\"claim\",\"sources\",\"confidence\"}],\"relations\":[{\"name\",\"from\",\"to\",\"claim\",\"sources\",\"confidence\"}]}.",
            "An entity's \"id\" only links relations to it within this response; its \"name\" is the entity as the records write it. Every claim must cite at least one source id.",
            "",
            "Records:",
        ]) + Environment.NewLine;
        Assert.StartsWith(expectedPreamble, model.LastPrompt);
    }

    [Fact]
    public async Task ProposeAsync_hands_the_model_client_a_strict_response_schema()
    {
        var model = new StubModelClient("""{"entities":[],"relations":[]}""");
        var proposer = new SinglePassOntologyProposer(model);

        await proposer.ProposeAsync(declaredStructures: [], records: [OneRecord("rec-1")], TestContext.Current.CancellationToken);

        var schema = Assert.NotNull(model.LastRequest?.ResponseSchema);
        foreach (var level in new[] { schema, ItemSchema(schema, "entities"), ItemSchema(schema, "relations") })
        {
            Assert.Equal("object", level.GetProperty("type").GetString());
            Assert.False(level.GetProperty("additionalProperties").GetBoolean());
            Assert.Equal(PropertyNames(level).Order(), level.GetProperty("required").EnumerateArray().Select(n => n.GetString()!).Order());
        }
    }

    [Fact]
    public async Task ProposeAsync_response_schema_names_the_same_fields_as_the_prompt()
    {
        // The response shape lives in three places: the prompt sentence (all a client that ignores
        // the schema has), the schema, and the parser. This holds the sentence and the schema to the
        // same field names; the next test holds the parser to reading every one of them.
        var model = new StubModelClient("""{"entities":[],"relations":[]}""");
        var proposer = new SinglePassOntologyProposer(model);

        await proposer.ProposeAsync(declaredStructures: [], records: [OneRecord("rec-1")], TestContext.Current.CancellationToken);

        var schema = model.LastRequest!.ResponseSchema!.Value;
        var sentence = model.LastPrompt!.Split(Environment.NewLine).Single(line => line.StartsWith("Respond with JSON only:", StringComparison.Ordinal));
        var match = Regex.Match(sentence, """\{"entities":\[\{(?<entity>[^}]*)\}\],"relations":\[\{(?<relation>[^}]*)\}\]\}""");
        Assert.True(match.Success, sentence);
        Assert.Equal(["entities", "relations"], PropertyNames(schema));
        Assert.Equal(QuotedNames(match.Groups["entity"].Value), PropertyNames(ItemSchema(schema, "entities")));
        Assert.Equal(QuotedNames(match.Groups["relation"].Value), PropertyNames(ItemSchema(schema, "relations")));
    }

    [Fact]
    public async Task ProposeAsync_parses_a_response_that_fills_every_schema_field_without_rejections()
    {
        // Built from the schema itself rather than written by hand, so a field the schema demands but
        // the parser does not read (a renamed property) shows up as a MissingField rejection here.
        var capture = new StubModelClient("""{"entities":[],"relations":[]}""");
        await new SinglePassOntologyProposer(capture).ProposeAsync(declaredStructures: [], records: [OneRecord("rec-1")], TestContext.Current.CancellationToken);
        var schema = capture.LastRequest!.ResponseSchema!.Value;

        var response = new JsonObject
        {
            ["entities"] = new JsonArray(Fill(ItemSchema(schema, "entities"), text: "e1")),
            ["relations"] = new JsonArray(Fill(ItemSchema(schema, "relations"), text: "e1")),
        };
        var proposer = new SinglePassOntologyProposer(new StubModelClient(response.ToJsonString()));

        var proposal = await proposer.ProposeAsync(declaredStructures: [], records: [OneRecord("rec-1")], TestContext.Current.CancellationToken);

        Assert.Empty(proposal.Rejections);
        Assert.Single(proposal.Entities);
        Assert.Single(proposal.Relations);
    }

    private static JsonElement ItemSchema(JsonElement schema, string arrayProperty) =>
        schema.GetProperty("properties").GetProperty(arrayProperty).GetProperty("items");

    private static List<string> PropertyNames(JsonElement objectSchema) =>
        objectSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList();

    private static List<string> QuotedNames(string text) =>
        Regex.Matches(text, "\"(?<name>[^\"]+)\"").Select(m => m.Groups["name"].Value).ToList();

    // A value of each property's declared type: every string is the given text (so ids, relation
    // ends and sources line up on one entity and one record), arrays hold one such string.
    private static JsonObject Fill(JsonElement objectSchema, string text)
    {
        var filled = new JsonObject();
        foreach (var property in objectSchema.GetProperty("properties").EnumerateObject())
        {
            filled[property.Name] = property.Value.GetProperty("type").GetString() switch
            {
                "string" => text,
                "number" => 0.5,
                "array" => new JsonArray("rec-1"),
                var other => throw new InvalidOperationException($"no filler for schema type {other}"),
            };
        }

        return filled;
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
