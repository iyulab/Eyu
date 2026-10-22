using Eyu.Core.Grounding;
using Eyu.Core.Proposals;
using Xunit;

namespace Eyu.Core.Tests.Quality;

public sealed class Llms4OlReuseCorpusTests
{
    private const string Json = """
        [
          {
            "id": "s1",
            "context": "Title: Poultry\n\nContent:\nA turkey processed carcass is a piece of turkey.",
            "terms": ["turkey processed carcass"],
            "types": ["piece of turkey", "turkey processed carcass"],
            "initial-primitive-ontology-triples": [
              ["turkey fat", "is-a", "piece of turkey"],
              ["turkey fat", "is a", "fat"],
              ["piece of turkey", "disjoint with", "beef cut"],
              ["drumstick", "instance-of", "poultry part"],
              ["Turkey Fat", "is-a", "turkey-fat"]
            ],
            "extended-primitive-ontology-triples": [["turkey processed carcass", "is-a", "piece of turkey"]]
          }
        ]
        """;

    private static Llms4OlReuseCorpus.Sample Sample => Llms4OlReuseCorpus.Parse(Json).Single();

    private static GroundedClaim Cite() => GroundedClaim.Create("the text says so", [new SourceRef("s1")]);

    [Fact]
    public void A_sample_reads_with_relation_names_in_hyphen_form()
    {
        var sample = Sample;

        Assert.Equal("s1", sample.Id);
        Assert.Contains(sample.Initial, t => t[1] == "disjoint-with");
        Assert.Contains(sample.Initial, t => t[0] == "turkey fat" && t[1] == "is-a" && t[2] == "fat");
        Assert.Single(sample.Extended);
    }

    [Fact]
    public void The_initial_ontology_is_the_declaration_one_structure_per_subject()
    {
        var declarations = Llms4OlReuseCorpus.Declarations(Sample);

        var fat = Assert.Single(declarations, d => d.Subject.Value == "turkey fat");
        Assert.Equal(["piece of turkey", "fat"], fat.SuperTypes);
        var piece = Assert.Single(declarations, d => d.Subject.Value == "piece of turkey");
        var disjoint = Assert.Single(piece.Relations);
        Assert.Equal("disjoint-with", disjoint.Name);
        Assert.Equal("beef cut", disjoint.Target.Value);
        Assert.DoesNotContain(declarations, d => d.Subject.Value == "drumstick"); // instance-of declares no subject
    }

    [Fact]
    public void The_types_in_play_are_known_types_less_the_declared_ones()
    {
        var sample = Sample;
        var prior = Llms4OlReuseCorpus.Prior(sample, Llms4OlReuseCorpus.Declarations(sample));

        Assert.Equal(["turkey processed carcass", "poultry part"], prior.KnownTypes.Select(t => t.Name));
    }

    [Fact]
    public void The_extension_is_what_the_proposal_adds_beyond_the_initial_ontology()
    {
        var sample = Sample;
        var proposal = new OntologyProposal(
            [
                EntityProposal.Create("e1", "turkey processed carcass", "piece of turkey", Cite(), VocabularyOrigin.Acquired, 0.8),
                EntityProposal.Create("e2", "Line 4", "ProductionLine", Cite(), VocabularyOrigin.Acquired, 0.8),
            ],
            [],
            [])
        {
            TypeRelations =
            [
                TypeRelationProposal.Create("turkey processed carcass", "piece of turkey", Cite(), 0.9),
                TypeRelationProposal.Create("Turkey Fat", "Piece of Turkey", Cite(), 0.9), // already initial
                TypeRelationProposal.Create("turkey fat", "fat", Cite(), 0.9, ProposalBasis.Declared),
                TypeRelationProposal.Create("Carcass", "Animal Product", Cite(), 0.5),
            ],
        };

        var all = Llms4OlReuseCorpus.Extension(sample, proposal, Llms4OlReuseCorpus.Variant.All);
        var vocabulary = Llms4OlReuseCorpus.Extension(sample, proposal, Llms4OlReuseCorpus.Variant.Vocabulary);

        // Line 4 is no term the sample gives to type, so it is not typed.
        Assert.Equal(
            [["turkey processed carcass", "is-a", "piece of turkey"], ["Carcass", "is-a", "Animal Product"], ["turkey processed carcass", "instance-of", "piece of turkey"]],
            all);
        Assert.Equal(
            [["turkey processed carcass", "is-a", "piece of turkey"], ["turkey processed carcass", "instance-of", "piece of turkey"]],
            vocabulary);
    }

    [Fact]
    public void A_term_names_an_entity_written_with_other_separators_and_keeps_its_own_spelling()
    {
        var sample = Llms4OlReuseCorpus.Parse(Json.Replace("\"terms\": [\"turkey processed carcass\"]", "\"terms\": [\"turkey processed carcass\", \"mm topic 8360\", \"a adm1h\"]", StringComparison.Ordinal)).Single();
        var proposal = new OntologyProposal(
            [
                EntityProposal.Create("e1", "MM_Topic_8360", "piece of turkey", Cite(), VocabularyOrigin.Acquired, 0.8),
                EntityProposal.Create("e2", "A.ADM1H", "piece of turkey", Cite(), VocabularyOrigin.Acquired, 0.8),
                EntityProposal.Create("e3", "mm topic 83600", "piece of turkey", Cite(), VocabularyOrigin.Acquired, 0.8),
            ],
            [],
            []);

        var extension = Llms4OlReuseCorpus.Extension(sample, proposal, Llms4OlReuseCorpus.Variant.All);

        Assert.Equal([["mm topic 8360", "instance-of", "piece of turkey"], ["a adm1h", "instance-of", "piece of turkey"]], extension);
    }

    [Fact]
    public void A_line_carries_the_prediction_beside_the_gold()
    {
        var line = Llms4OlReuseCorpus.ToJsonLine(Sample, [["a", "is-a", "b"]]);

        Assert.Equal("""{"id":"s1","predicted":[["a","is-a","b"]],"gold":[["turkey processed carcass","is-a","piece of turkey"]]}""" + "\n", line);
    }
}
