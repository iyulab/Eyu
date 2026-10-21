using System.Text.Json;
using Eyu.Core.Grounding;
using Eyu.Core.Primitives;
using Eyu.Core.Proposals;
using Xunit;

namespace Eyu.Core.Tests.Quality;

public class Text2KgBenchCorpusTests
{
    private const string Ontology = """
        {"title":"Movie","id":"1","concepts":[{"qid":"Q11424","label":"film"},{"qid":"Q5","label":"human"}],
         "relations":[{"pid":"P57","label":"director","domain":"Q11424","range":"Q5"},
                      {"pid":"P577","label":"publication date","domain":"Q11424","range":""},
                      {"pid":"P144","label":"based on","domain":"Q11424","range":"Q47461344"},
                      {"pid":"P1","label":"orphan","domain":"Q999","range":"Q5"},
                      {"pid":"P57","label":"director","domain":"Q11424","range":"Q5"}]}
        """;

    [Fact]
    public void Each_concept_is_a_structure_and_each_relation_is_declared_on_its_domain()
    {
        var (declarations, undeclarable) = Text2KgBenchCorpus.Declarations(JsonDocument.Parse(Ontology).RootElement);

        Assert.Equal(["film", "human"], declarations.Select(d => d.Subject.Value));
        Assert.Equal(
            [("director", "human"), ("publication date", Text2KgBenchCorpus.ValueTarget), ("based on", "Q47461344")],
            declarations[0].Relations.Select(r => (r.Name, r.Target.Value)));
        Assert.Empty(declarations[1].Relations);
        Assert.Equal(["orphan"], undeclarable);
    }

    private static GroundedClaim Claim(params string[] sentences) => GroundedClaim.Create("c", [.. sentences.Select(s => new SourceRef(s))]);

    private static readonly OntologyProposal Proposal = new(
        [
            EntityProposal.Create("e1", "Bleach: Hell Verse", "film", Claim("s1"), VocabularyOrigin.Acquired, 0.9, ProposalBasis.Declared),
            EntityProposal.Create("e2", "Noriyuki Abe", "human", Claim("s1"), VocabularyOrigin.Acquired, 0.9, ProposalBasis.Declared),
        ],
        [
            RelationProposal.Create("director", "e1", "e2", Claim("s1", "s2"), VocabularyOrigin.Acquired, 0.9, ProposalBasis.Declared),
            RelationProposal.Create("animated_by", "e1", "e2", Claim("s1"), VocabularyOrigin.Acquired, 0.5),
        ],
        []);

    [Fact]
    public void A_relation_is_a_triple_of_every_sentence_its_claim_cites()
    {
        var triples = Text2KgBenchCorpus.Triples([Proposal], ["s1", "s2", "s3"], declaredOnly: false);

        Assert.Equal(2, triples["s1"].Count);
        Assert.Equal(["Bleach: Hell Verse", "director", "Noriyuki Abe"], Assert.Single(triples["s2"]));
        Assert.Empty(triples["s3"]);
    }

    [Fact]
    public void Declared_only_keeps_the_relations_the_ontology_declares()
    {
        var triples = Text2KgBenchCorpus.Triples([Proposal], ["s1"], declaredOnly: true);

        Assert.Equal("director", Assert.Single(triples["s1"])[1]);
    }

    [Fact]
    public void The_scorer_input_has_one_line_per_sentence()
    {
        var text = Text2KgBenchCorpus.ToJsonLines(["s1", "s3"], Text2KgBenchCorpus.Triples([Proposal], ["s1", "s3"], declaredOnly: true));

        var lines = text.TrimEnd('\n').Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Equal("""{"id":"s1","triples":[["Bleach: Hell Verse","director","Noriyuki Abe"]]}""", lines[0]);
        Assert.Equal("""{"id":"s3","triples":[]}""", lines[1]);
    }

    // The scorer checks a system triple's relation against the ontology's labels with spaces as
    // underscores — the form the benchmark's baselines write; a label with spaces reads as a hallucination.
    [Fact]
    public void A_relation_label_is_written_with_underscores_as_the_system_outputs_write_it()
    {
        var proposal = new OntologyProposal(
            Proposal.Entities,
            [RelationProposal.Create("cast member", "e1", "e2", Claim("s1"), VocabularyOrigin.Acquired, 0.9, ProposalBasis.Declared)],
            []);

        var triples = Text2KgBenchCorpus.Triples([proposal], ["s1"], declaredOnly: true);

        Assert.Equal("cast_member", Assert.Single(triples["s1"])[1]);
    }
}
