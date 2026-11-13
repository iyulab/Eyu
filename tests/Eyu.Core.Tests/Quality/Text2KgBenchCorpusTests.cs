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
        var (declarations, undeclarable) = Text2KgBenchCorpus.Declarations(JsonElement.Parse(Ontology));

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

    [Fact]
    public void A_rejected_relation_carries_the_names_and_types_of_its_ends()
    {
        var proposal = new OntologyProposal(
            Proposal.Entities,
            [],
            [
                new ProposalRejection(ProposalElement.Relation, "director", RejectionReason.ContradictsDeclaration,
                    "relation \"director\" (e2 -> e1) uses the declared name for ends no declaration of it describes (film -> human)"),
                new ProposalRejection(ProposalElement.Entity, "e9", RejectionReason.MissingField, "entity e9 cites no source"),
            ]);

        var rejections = Text2KgBenchCorpus.Rejections(proposal).ToList();

        Assert.Equal(("Noriyuki Abe", "human", "Bleach: Hell Verse", "film"), (rejections[0].From, rejections[0].FromType, rejections[0].To, rejections[0].ToType));
        Assert.Equal(("ContradictsDeclaration", "director"), (rejections[0].Reason, rejections[0].Name));
        Assert.Null(rejections[1].From);
    }

    private static readonly string[] SentenceIds = [.. Enumerable.Range(1, 60).Select(i => $"s{i:000}")];

    [Fact]
    public void A_sample_is_the_same_for_the_same_seed_and_differs_for_another()
    {
        var first = Text2KgBenchCorpus.Sample(SentenceIds, 10, seed: 1);

        Assert.Equal(first, Text2KgBenchCorpus.Sample(SentenceIds, 10, seed: 1));
        Assert.NotEqual(first, Text2KgBenchCorpus.Sample(SentenceIds, 10, seed: 2));
    }

    [Fact]
    public void A_sample_is_the_asked_number_of_the_given_ids_in_their_own_order()
    {
        var sample = Text2KgBenchCorpus.Sample(SentenceIds, 10, seed: 7);

        Assert.Equal(10, sample.Length);
        Assert.Equal(sample.Distinct(StringComparer.Ordinal), sample);
        Assert.Equal(sample.OrderBy(id => Array.IndexOf(SentenceIds, id)), sample);
    }

    [Fact]
    public void A_sample_does_not_depend_on_how_many_sentences_come_after_the_ones_it_keeps()
    {
        // The order is by hash of seed and id, so an id's place does not move when others are added: the first 40 ids' sample is
        // what the 60-id sample keeps among the first 40, as long as the 60-id sample took them.
        var whole = Text2KgBenchCorpus.Sample(SentenceIds, 60, seed: 3);

        Assert.Equal(SentenceIds, whole);
    }

    [Fact]
    public void A_sample_pins_the_hash_order_so_a_round_can_be_reproduced_later()
    {
        Assert.Equal(["s015", "s041", "s047"], Text2KgBenchCorpus.Sample(SentenceIds, 3, seed: 20261110));
    }

    [Theory]
    [InlineData(60)]
    [InlineData(100)]
    public void A_count_at_or_past_the_number_of_ids_is_all_of_them(int count)
    {
        Assert.Equal(SentenceIds, Text2KgBenchCorpus.Sample(SentenceIds, count, seed: 5));
    }

    [Fact]
    public void A_sample_counts_a_repeated_id_once()
    {
        Assert.Equal(["a", "b"], Text2KgBenchCorpus.Sample(["a", "b", "a"], 5, seed: 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_sample_of_nothing_is_refused(int count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Text2KgBenchCorpus.Sample(SentenceIds, count, seed: 1));
    }

    private static JsonElement Patterns(string config) => JsonElement.Parse(config).GetProperty("path_patterns");

    [Fact]
    public void The_scorer_averages_over_the_verified_sentences_unless_told_otherwise()
    {
        var patterns = Patterns(Text2KgBenchCorpus.EvalConfig("/bench/repo", "wikidata_tekgen", "/out/all", ["1_movie"]));

        Assert.Equal(Path.Combine("/bench/repo", "data", "wikidata_tekgen", "manually_verified_sentences", "selected_ont_$$onto$$.txt"), patterns.GetProperty("selected_ids").GetString());
    }

    /// <summary>
    /// The scorer divides what it summed over the sentences it was given by the length of this list. A sample scored against the
    /// verified list reads every score at the sample's share of it — a fifth of it, for twenty of a hundred.
    /// </summary>
    [Fact]
    public void A_sampled_run_names_its_own_id_files_so_the_scorers_denominator_is_the_sample()
    {
        var config = Text2KgBenchCorpus.EvalConfig("/bench/repo", "wikidata_tekgen", "/out/all", ["1_movie", "3_sport"], "/out/sample/selected_ont_$$onto$$.txt");

        Assert.Equal("/out/sample/selected_ont_$$onto$$.txt", Patterns(config).GetProperty("selected_ids").GetString());
        Assert.Equal(["1_movie", "3_sport"], JsonElement.Parse(config).GetProperty("onto_list").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void The_scorer_reads_the_runs_outputs_and_the_benchmarks_ground_truth_and_ontologies()
    {
        var patterns = Patterns(Text2KgBenchCorpus.EvalConfig("/bench/repo", "wikidata_tekgen", "/out/declared", ["1_movie"]));

        Assert.Equal(Path.Combine("/out/declared", "ont_$$onto$$_responses.jsonl"), patterns.GetProperty("sys").GetString());
        Assert.Equal(Path.Combine("/out/declared", "ont_$$onto$$_stats.jsonl"), patterns.GetProperty("output").GetString());
        Assert.Equal(Path.Combine("/bench/repo", "data", "wikidata_tekgen", "ground_truth", "ont_$$onto$$_ground_truth.jsonl"), patterns.GetProperty("gt").GetString());
        Assert.Equal(Path.Combine("/bench/repo", "data", "wikidata_tekgen", "ontologies", "$$onto$$_ontology.json"), patterns.GetProperty("onto").GetString());
    }
}
