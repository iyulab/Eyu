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
}
