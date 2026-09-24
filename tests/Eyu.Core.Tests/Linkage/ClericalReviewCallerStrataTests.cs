using Eyu.Core.Linkage;
using Xunit;

namespace Eyu.Core.Tests.Linkage;

/// <summary>
/// A review whose clusters or strata do not come from the pre-filter — clusters a model made, strata
/// from a similarity band computed outside the mixture — draws and scores through the same arithmetic.
/// </summary>
public class ClericalReviewCallerStrataTests
{
    private const string Reviewer = "r1";

    private static IReadOnlyList<string> Ids(string prefix, int count) =>
        Enumerable.Range(0, count).Select(i => $"{prefix}{i:D2}").ToList();

    [Fact]
    public void A_caller_formed_population_is_drawn_in_the_order_given_and_reproducibly()
    {
        var merged = Ids("m", 10);
        var single = Ids("s", 3);
        IReadOnlyList<PopulationStratum> population =
        [
            new("merged-low", merged),
            new("single-high", single),
            new("empty", []),
        ];

        var first = ClericalReviewSampler.DrawPilot(population, perStratum: 4, seed: 7);
        var again = ClericalReviewSampler.DrawPilot(
            [new("merged-low", merged.Reverse().ToList()), new("single-high", single), new("empty", [])],
            perStratum: 4,
            seed: 7);

        Assert.Equal(["merged-low", "single-high"], first.Strata.Select(s => s.Name));
        Assert.Equal(10, first.Strata[0].PopulationSize);
        Assert.Equal(4, first.Strata[0].SelectedRecordIds.Count);
        Assert.Equal(single, first.Strata[1].SelectedRecordIds);
        Assert.Equal(ReviewSampleCaveat.StratumEmpty | ReviewSampleCaveat.StratumTakenWhole, first.Caveats);
        Assert.Equal(first.Strata[0].SelectedRecordIds, again.Strata[0].SelectedRecordIds);
    }

    [Fact]
    public void Strata_must_partition_the_population()
    {
        Assert.Throws<ArgumentException>(() => ClericalReviewSampler.DrawPilot(
            [new PopulationStratum("a", ["x", "y"]), new PopulationStratum("b", ["y"])], 1, 0));
        Assert.Throws<ArgumentException>(() => ClericalReviewSampler.DrawPilot(
            [new PopulationStratum("a", ["x"]), new PopulationStratum("a", ["y"])], 1, 0));
        Assert.Throws<ArgumentException>(() => ClericalReviewSampler.DrawPilot(
            [new PopulationStratum("", ["x"])], 1, 0));
    }

    [Fact]
    public void The_pre_filter_draw_is_the_caller_draw_over_its_three_strata()
    {
        // The protocol walkthrough's batch: all three strata are populated.
        var analysis = ClericalReviewWalkthroughTests.SixteenRecordBatch();
        var strata = ClericalReviewSampler.StratifyRecords(analysis);
        var population = Enum.GetValues<ReviewStratumKind>()
            .Select(kind => new PopulationStratum(kind.ToString(), strata.Where(e => e.Value == kind).Select(e => e.Key).ToList()))
            .ToList();

        var viaAnalysis = ClericalReviewSampler.DrawPilot(analysis, perStratum: 3, seed: 11);
        var viaPopulation = ClericalReviewSampler.DrawPilot(population, perStratum: 3, seed: 11);

        Assert.Equal(3, viaAnalysis.Strata.Count);

        Assert.Equal(viaPopulation.Strata.Select(s => (s.Name, s.PopulationSize, string.Join(",", s.SelectedRecordIds))),
            viaAnalysis.Strata.Select(s => (s.Name, s.PopulationSize, string.Join(",", s.SelectedRecordIds))));
    }

    // Four records of two meanings; the model kept a1 and a2 together, left a3 out of them and put it
    // with b1. The reviewer is shown a3 as a candidate for a1, and a1, a2 as candidates for a3.
    [Fact]
    public void Clusters_a_model_made_are_scored_without_a_linkage_analysis()
    {
        IReadOnlyList<RecordCluster> clusters = [new(["a1", "a2"]), new(["a3", "b1"])];
        var candidates = new Dictionary<string, IReadOnlyCollection<string>>
        {
            ["a1"] = ["a3"],
            ["a3"] = ["a1", "a2"],
        };
        var sample = new ReviewSample([new SampledStratum("merged", 4, ["a1", "a3"])], 2, 0, ReviewSampleCaveat.None);
        ReviewVerdict[] verdicts =
        [
            new("a1", "a2", ReviewOutcome.Same, Reviewer),
            new("a1", "a3", ReviewOutcome.Same, Reviewer),
            new("a3", "b1", ReviewOutcome.Different, Reviewer),
            new("a3", "a1", ReviewOutcome.Same, Reviewer),
            new("a3", "a2", ReviewOutcome.Same, Reviewer),
        ];

        var scoring = ClericalReviewScoring.Score(clusters, candidates, sample, verdicts, Reviewer);

        var precision = Assert.Single(scoring.Precision);
        var recall = Assert.Single(scoring.Recall);
        Assert.Equal("merged", precision.Name);
        Assert.Equal([1.0, 0.5], precision.ReviewedScores);
        Assert.Equal(2.0 / 3, recall.ReviewedScores[0], 12);
        Assert.Equal(1.0 / 3, recall.ReviewedScores[1], 12);
        Assert.Equal(new StratumExclusion("merged", 2, 0), Assert.Single(scoring.Exclusions));
    }

    [Fact]
    public void A_sampled_record_no_cluster_places_is_refused()
    {
        var sample = new ReviewSample([new SampledStratum("single", 1, ["x"])], 1, 0, ReviewSampleCaveat.None);

        Assert.Throws<ArgumentException>(() => ClericalReviewScoring.Score(
            [new RecordCluster(["y"])], new Dictionary<string, IReadOnlyCollection<string>>(), sample,
            [new ReviewVerdict("x", "y", ReviewOutcome.Different, Reviewer)], Reviewer));
    }
}
