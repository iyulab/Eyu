using Eyu.Core.Grounding;
using Eyu.Core.Proposals;
using Xunit;

namespace Eyu.Core.Tests.Quality;

public class CompetencyQuestionReachTests
{
    private static GroundedClaim Claim(string text) =>
        GroundedClaim.Create(text, [new SourceRef("rec-1")]);

    private static OntologyProposal Proposal(string[] entityTypes, string[] relationNames)
    {
        var entities = entityTypes
            .Select((type, index) => EntityProposal.Create(
                $"e{index}", type, Claim($"a {type} appears in the records"), VocabularyOrigin.Innate, 0.9))
            .ToList();
        var relations = relationNames
            .Select(name => RelationProposal.Create(
                name, "e0", "e0", Claim($"{name} holds"), VocabularyOrigin.Innate, 0.9))
            .ToList();
        return new OntologyProposal(entities, relations);
    }

    [Fact]
    public void A_question_is_reached_when_every_concept_it_names_is_in_the_vocabulary()
    {
        var proposal = Proposal(["Plant", "ReactorEvent"], ["occurredAt"]);
        var question = new CompetencyQuestion("Which events belong to a plant?", ["plant", "event", "occurred"]);

        Assert.True(CompetencyQuestionReach.Reaches(question, CompetencyQuestionReach.Vocabulary(proposal)));
    }

    // An unreached question is the whole point of the axis: it names the missing entity or
    // relation. Here two entity types exist and nothing relates them, so nothing can answer which
    // event belongs to which plant.
    [Fact]
    public void One_missing_concept_is_enough_to_leave_a_question_unreached()
    {
        var proposal = Proposal(["Plant", "ReactorEvent"], []);
        var question = new CompetencyQuestion("Which events belong to a plant?", ["plant", "event", "occurred|belongs"]);

        Assert.False(CompetencyQuestionReach.Reaches(question, CompetencyQuestionReach.Vocabulary(proposal)));
    }

    [Fact]
    public void Any_one_alternative_reaches_the_concept()
    {
        var vocabulary = CompetencyQuestionReach.Vocabulary(Proposal(["Occurrence"], []));

        Assert.True(CompetencyQuestionReach.Reaches(
            new CompetencyQuestion("q", ["event|report|occurrence"]), vocabulary));
    }

    // Claims quote the records, so scoring against them would report that the data contains the
    // question's words — true before any ontology is proposed at all.
    [Fact]
    public void Claim_text_is_not_part_of_the_vocabulary()
    {
        var entity = EntityProposal.Create(
            "e0", "Thing", Claim("this plant reported an event"), VocabularyOrigin.Innate, 0.9);
        var proposal = new OntologyProposal([entity], []);

        Assert.Equal(["thing"], CompetencyQuestionReach.Vocabulary(proposal));
        Assert.False(CompetencyQuestionReach.Reaches(
            new CompetencyQuestion("q", ["plant"]), CompetencyQuestionReach.Vocabulary(proposal)));
    }

    // The guard the questions themselves are held to: substring matching makes a two-letter
    // alternative reachable from any type name that happens to contain it, so a question written
    // that way passes without testing the structure it names.
    [Fact]
    public void An_alternative_too_short_to_mean_anything_is_refused_when_the_question_is_written()
    {
        var tooShort = Assert.Throws<ArgumentException>(() =>
            new CompetencyQuestion("Which events belong to a plant?", ["plant", "at|occurred"]));

        Assert.Contains("at", tooShort.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => new CompetencyQuestion("q", []));
    }

    [Fact]
    public void Matching_is_case_insensitive_on_the_vocabulary_side()
    {
        var vocabulary = CompetencyQuestionReach.Vocabulary(Proposal(["AircraftComponent"], []));

        Assert.True(CompetencyQuestionReach.Reaches(
            new CompetencyQuestion("q", ["aircraft", "component"]), vocabulary));
    }

    // The point of the diagnostic: the nuclear catalog's procedural-cause question misses because
    // no type says "procedure", while the model did express causation -- as a generic type whose
    // claim carries the specific cause. Reporting the stand-in is what separates "the model
    // ignored this" from "the model put it somewhere the question cannot reach".
    [Fact]
    public void A_missed_question_reports_the_entities_standing_for_the_concepts_it_did_reach()
    {
        var proposal = Proposal(["ReactorEvent", "Cause"], ["caused_by"]);
        var question = new CompetencyQuestion(
            "Which events are attributed to a procedural deficiency?",
            ["procedure|process|guidance|work", "cause|deficiency|inadequate|reason"]);

        Assert.False(CompetencyQuestionReach.Reaches(question, CompetencyQuestionReach.Vocabulary(proposal)));

        var reached = CompetencyQuestionReach.ReachedConcepts(question, proposal);

        var only = Assert.Single(reached);
        Assert.Equal("cause|deficiency|inadequate|reason", only.Concept);
        Assert.Equal("Cause", Assert.Single(only.Entities).EntityType);
    }

    // A concept can be carried by a relation name with no entity behind it. That is a different
    // finding from "never reached" -- the empty list keeps the two apart instead of collapsing
    // them into one silent absence.
    [Fact]
    public void A_concept_carried_only_by_a_relation_comes_back_with_no_entity()
    {
        var proposal = Proposal(["ReactorEvent"], ["caused_by"]);
        var question = new CompetencyQuestion(
            "Which events are attributed to a procedural deficiency?",
            ["procedure|process|guidance|work", "cause|deficiency|inadequate|reason"]);

        var only = Assert.Single(CompetencyQuestionReach.ReachedConcepts(question, proposal));

        Assert.Equal("cause|deficiency|inadequate|reason", only.Concept);
        Assert.Empty(only.Entities);
    }

    // Nothing is reported for a concept the proposal never reached -- the report already names it
    // as the miss, and repeating it as an empty stand-in would read as a finding.
    [Fact]
    public void A_concept_that_was_never_reached_is_not_reported_as_a_stand_in()
    {
        var proposal = Proposal(["ReactorEvent"], []);
        var question = new CompetencyQuestion("Which plant?", ["plant|site", "event|report"]);

        var only = Assert.Single(CompetencyQuestionReach.ReachedConcepts(question, proposal));

        Assert.Equal("event|report", only.Concept);
    }

    // Alternatives are matched case-insensitively on both sides. A capitalised alternative used to
    // match nothing at all, because only the vocabulary was lowered -- a question that asks less
    // than its author wrote is exactly what this check exists to prevent.
    [Fact]
    public void A_capitalised_alternative_reaches_the_same_types_a_lowercase_one_does()
    {
        var proposal = Proposal(["PowerPlant"], []);

        Assert.True(CompetencyQuestionReach.Reaches(
            new CompetencyQuestion("Which plant?", ["Plant"]),
            CompetencyQuestionReach.Vocabulary(proposal)));
    }

    // A live run named the plant a Facility, and two questions that had reached on every previous
    // run went unreached -- not because the structure was missing but because the concept listed
    // three spellings and the model used a fourth. Alternatives exist so the check measures
    // structure rather than wording; an incomplete list quietly turns it back into a wording check.
    [Fact]
    public void The_plant_concept_is_reached_by_every_spelling_the_catalog_lists()
    {
        var concept = "plant|unit|site|facility|station";

        foreach (var type in new[] { "Plant", "PowerPlant", "Unit", "Site", "Facility", "Station" })
        {
            Assert.True(
                CompetencyQuestionReach.Reaches(
                    new CompetencyQuestion("Which plant?", [concept]),
                    CompetencyQuestionReach.Vocabulary(Proposal([type], []))),
                $"type \"{type}\" should reach the plant concept");
        }
    }
}
