using Eyu.Core.Declared;
using Eyu.Core.Primitives;
using Xunit;

namespace Eyu.Core.Tests.Quality;

public class DeclarationLadderTests
{
    private static DeclarationLadder Ladder(int fields, int relationsAfterEachField)
    {
        var items = new List<DeclarationItem>();
        for (var f = 0; f < fields; f++)
        {
            items.Add(new DeclarationItem.Field(new DeclaredField($"field{f}")));
            for (var r = 0; r < relationsAfterEachField; r++)
            {
                items.Add(new DeclarationItem.Relation(new DeclaredRelation($"rel{f}_{r}", SubjectRef.Create($"target{f}_{r}"), ViaField: $"field{f}")));
            }
        }

        return new DeclarationLadder(SubjectRef.Create("subject"), items);
    }

    [Fact]
    public void The_bottom_rung_declares_nothing_and_the_top_rung_declares_everything()
    {
        var ladder = Ladder(fields: 3, relationsAfterEachField: 1);

        var levels = ladder.Levels(steps: 3);

        Assert.Equal(4, levels.Count);
        Assert.Equal(0.0, levels[0].Completeness);
        Assert.Null(levels[0].Structure);
        Assert.Equal(1.0, levels[^1].Completeness);
        Assert.Equal(3, levels[^1].Structure!.Fields.Count);
        Assert.Equal(3, levels[^1].Structure!.Relations.Count);
    }

    // Six items over three steps keep 0, 2, 4, 6 — prefixes in form order, so the second rung is
    // the first field and the relation it carries, not two fields.
    [Fact]
    public void Rungs_are_prefixes_of_the_form_ordered_items()
    {
        var ladder = Ladder(fields: 3, relationsAfterEachField: 1);

        var levels = ladder.Levels(steps: 3);

        Assert.Equal([0, 2, 4, 6], levels.Select(l => l.ItemsKept));
        var second = levels[1].Structure!;
        Assert.Equal(["field0"], second.Fields.Select(f => f.Name));
        Assert.Equal(["rel0_0"], second.Relations.Select(r => r.Name));
    }

    // Eight items over three steps: 8/3 = 2.67 rounds to 3, 16/3 = 5.33 rounds to 5. Kept counts
    // are rounded rather than truncated so the middle rungs sit as close to their nominal
    // completeness as whole items allow, and the reported completeness is the nominal step.
    [Fact]
    public void Kept_counts_round_to_the_nearest_whole_item()
    {
        var ladder = Ladder(fields: 4, relationsAfterEachField: 1);

        var levels = ladder.Levels(steps: 3);

        Assert.Equal([0, 3, 5, 8], levels.Select(l => l.ItemsKept));
        Assert.Equal([0.0, 1.0 / 3, 2.0 / 3, 1.0], levels.Select(l => l.Completeness));
    }

    [Fact]
    public void Every_rung_keeps_the_same_subject()
    {
        var ladder = Ladder(fields: 2, relationsAfterEachField: 1);

        var declared = ladder.Levels(steps: 2).Skip(1).Select(l => l.Structure!.Subject.Value);

        Assert.All(declared, subject => Assert.Equal("subject", subject));
    }

    // The declared vocabulary is what a question can be reached through before any model has
    // answered: the subject, relation names, and relation targets. Fields are not in it because
    // a declared field never becomes a type or a relation on its own.
    [Fact]
    public void Declared_vocabulary_carries_subject_relation_names_and_targets_but_not_fields()
    {
        var structure = new DeclaredStructure(
            SubjectRef.Create("Event"),
            [new DeclaredField("plant_name")],
            [new DeclaredRelation("occurred_at", SubjectRef.Create("Plant"), ViaField: "plant_name")]);

        var vocabulary = DeclarationLadder.DeclaredVocabulary(structure);

        Assert.Equal(["event", "occurred_at", "plant"], vocabulary);
        Assert.Empty(DeclarationLadder.DeclaredVocabulary(null));
    }

    [Fact]
    public void A_question_the_declaration_names_is_reached_by_the_declared_vocabulary_alone()
    {
        var structure = new DeclaredStructure(
            SubjectRef.Create("event"),
            [new DeclaredField("plant_name")],
            [new DeclaredRelation("occurred_at", SubjectRef.Create("plant"), ViaField: "plant_name")]);
        var named = new CompetencyQuestion("Which plant did an event occur at?", ["plant", "event", "occurred"]);
        var unnamed = new CompetencyQuestion("What caused it?", ["cause"]);

        var vocabulary = DeclarationLadder.DeclaredVocabulary(structure);

        Assert.True(CompetencyQuestionReach.Reaches(named, vocabulary));
        Assert.False(CompetencyQuestionReach.Reaches(unnamed, vocabulary));
    }

    // Two questions; the declaration names the first (event, plant, occurred_at), so only the
    // second is left to inference. A proposal that reaches both scores 1/1 inferred; one that
    // reaches only the named question scores 0/1 — plain reach would report 1/2 for it and hide
    // that everything it reached was handed to it.
    [Fact]
    public void Inferred_reach_counts_only_the_questions_the_declaration_did_not_name()
    {
        var structure = new DeclaredStructure(
            SubjectRef.Create("event"),
            [new DeclaredField("plant_name")],
            [new DeclaredRelation("occurred_at", SubjectRef.Create("plant"), ViaField: "plant_name")]);
        CompetencyQuestion[] questions =
        [
            new("Which plant did an event occur at?", ["plant", "event", "occurred"]),
            new("What caused it?", ["cause"]),
        ];

        var both = DeclarationLadder.InferredReach(questions, structure, ["event", "plant", "occurred_at", "root_cause"]);
        var namedOnly = DeclarationLadder.InferredReach(questions, structure, ["event", "plant", "occurred_at"]);
        var nothingDeclared = DeclarationLadder.InferredReach(questions, null, ["event", "plant", "occurred_at"]);

        Assert.Equal(new InferredReach(1, 1), both);
        Assert.Equal(1.0, both.Rate);
        Assert.Equal(new InferredReach(1, 0), namedOnly);
        Assert.Equal(new InferredReach(2, 1), nothingDeclared);
        Assert.Equal(0.5, nothingDeclared.Rate);
    }

    [Fact]
    public void Inferred_reach_is_undefined_when_the_declaration_names_every_question()
    {
        var structure = new DeclaredStructure(
            SubjectRef.Create("event"),
            [],
            [new DeclaredRelation("occurred_at", SubjectRef.Create("plant"))]);
        CompetencyQuestion[] questions = [new("Which plant did an event occur at?", ["plant", "event", "occurred"])];

        var reach = DeclarationLadder.InferredReach(questions, structure, ["event", "plant", "occurred_at"]);

        Assert.Equal(0, reach.Unnamed);
        Assert.True(double.IsNaN(reach.Rate));
    }

    [Fact]
    public void An_empty_declaration_and_a_ladder_without_steps_are_refused()
    {
        Assert.Throws<ArgumentException>(() => new DeclarationLadder(SubjectRef.Create("subject"), []));
        Assert.Throws<ArgumentOutOfRangeException>(() => Ladder(1, 1).Levels(steps: 0));
    }

    // The catalog's own ladders: every case's full declaration names every one of its questions,
    // otherwise the top rung could not reach 100% even with a perfectly compliant model and the
    // ablation would measure the catalog's gaps rather than the model's. Completeness must also
    // grow the named questions monotonically, or a rung would take structure away.
    [Fact]
    public void Each_catalog_declaration_names_all_of_its_questions_at_the_top_and_never_fewer_up_the_ladder()
    {
        foreach (var qualityCase in QualityCatalog.Cases)
        {
            var namedPerLevel = qualityCase.Declaration.Levels(steps: 3)
                .Select(level => DeclarationLadder.DeclaredVocabulary(level.Structure))
                .Select(vocabulary => qualityCase.Questions.Count(q => CompetencyQuestionReach.Reaches(q, vocabulary)))
                .ToList();

            Assert.Equal(0, namedPerLevel[0]);
            Assert.Equal(qualityCase.Questions.Length, namedPerLevel[^1]);
            for (var i = 1; i < namedPerLevel.Count; i++)
            {
                Assert.True(namedPerLevel[i] >= namedPerLevel[i - 1], $"{qualityCase.Name}: rung {i} names fewer questions than rung {i - 1}");
            }
        }
    }
}
