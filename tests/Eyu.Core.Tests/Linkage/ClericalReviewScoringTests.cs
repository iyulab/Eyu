using Eyu.Core.Linkage;
using Xunit;

namespace Eyu.Core.Tests.Linkage;

public class ClericalReviewScoringTests
{
    private const string Reviewer = "r1";

    private static ReviewVerdict V(string record, string candidate, ReviewOutcome outcome, string reviewer = Reviewer) =>
        new(record, candidate, outcome, reviewer);

    // A predicted cluster {a1, a2, a3, j1} where j1 is a namesake the system merged in. The
    // reviewer, shown a1's co-members, says a2 and a3 are the same entity and j1 is not.
    [Fact]
    public void A_false_merge_the_reviewer_rejects_costs_precision_and_nothing_else()
    {
        var scored = ClericalReviewScoring.ScoreRecord(
            "a1",
            ["a1", "a2", "a3", "j1"],
            [],
            [V("a1", "a2", ReviewOutcome.Same), V("a1", "a3", ReviewOutcome.Same), V("a1", "j1", ReviewOutcome.Different)]);

        Assert.False(scored.IsExcluded);
        Assert.Equal(0.75, scored.Score!.Precision, 12);
        Assert.Equal(1.0, scored.Score.Recall, 12);
        Assert.Equal(["j1"], scored.FalseMergeIds);
        Assert.Empty(scored.MissedMemberIds);
    }

    // The same verdicts pushed through the workaround a consumer reaches for when only a full
    // reference clustering can be scored -- leave the unreviewed a2 where the system put it, i.e.
    // in a reference cluster that still contains j1 -- give a1 a precision of 0.5: a2 is then
    // scored as if its cluster were {a1, a2, a3, j1} minus nothing. Composition gives 0.75, which
    // is what the reviewer actually established. Both numbers are pinned so the difference stays
    // visible.
    [Fact]
    public void Composition_scores_what_the_reviewer_established_not_what_a_patched_reference_implies()
    {
        var predicted = new[] { new RecordCluster(["a1", "a2", "a3", "j1"]) };
        var patchedReference = new[] { new RecordCluster(["a1", "a3"]), new RecordCluster(["a2", "j1"]) };
        var workaround = ClusteringMetrics.BCubedPerRecord(predicted, patchedReference)["a1"].Precision;

        var composed = ClericalReviewScoring.ScoreRecord(
            "a1",
            ["a1", "a2", "a3", "j1"],
            [],
            [V("a1", "a2", ReviewOutcome.Same), V("a1", "a3", ReviewOutcome.Same), V("a1", "j1", ReviewOutcome.Different)]).Score!.Precision;

        Assert.Equal(0.5, workaround, 12);
        Assert.Equal(0.75, composed, 12);
    }

    [Fact]
    public void A_member_the_system_missed_costs_recall()
    {
        // b is a singleton; the reviewer, shown candidates c and d, says c is the same entity.
        var scored = ClericalReviewScoring.ScoreRecord(
            "b",
            ["b"],
            ["c", "d"],
            [V("b", "c", ReviewOutcome.Same), V("b", "d", ReviewOutcome.Different)]);

        Assert.Equal(1.0, scored.Score!.Precision, 12);
        Assert.Equal(0.5, scored.Score.Recall, 12);
        Assert.Equal(["c"], scored.MissedMemberIds);
    }

    [Fact]
    public void Cannot_tell_excludes_the_record_and_names_what_was_unresolved()
    {
        var scored = ClericalReviewScoring.ScoreRecord(
            "a",
            ["a", "b"],
            ["c"],
            [V("a", "b", ReviewOutcome.Same), V("a", "c", ReviewOutcome.CannotTell)]);

        Assert.True(scored.IsExcluded);
        Assert.Null(scored.Score);
        Assert.Equal(["c"], scored.UnresolvedCandidateIds);
    }

    [Fact]
    public void A_missing_verdict_is_refused_by_name()
    {
        var exception = Assert.Throws<ArgumentException>(() => ClericalReviewScoring.ScoreRecord(
            "a",
            ["a", "b", "c"],
            ["d"],
            [V("a", "b", ReviewOutcome.Same)]));

        Assert.Contains("c, d", exception.Message);
        Assert.Equal("verdicts", exception.ParamName);
    }

    [Fact]
    public void Verdicts_from_two_reviewers_are_not_averaged_into_one_cluster()
    {
        var exception = Assert.Throws<ArgumentException>(() => ClericalReviewScoring.ScoreRecord(
            "a",
            ["a", "b"],
            [],
            [V("a", "b", ReviewOutcome.Same, "r1"), V("a", "b", ReviewOutcome.Different, "r2")]));

        Assert.Contains("more than one reviewer", exception.Message);
    }

    [Fact]
    public void A_verdict_on_a_candidate_never_shown_is_refused()
    {
        var exception = Assert.Throws<ArgumentException>(() => ClericalReviewScoring.ScoreRecord(
            "a",
            ["a"],
            [],
            [V("a", "z", ReviewOutcome.Same)]));

        Assert.Contains("never shown", exception.Message);
    }

    // End to end: a batch, a pilot draw, one reviewer's verdicts on every candidate of every
    // sampled record, and the result feeding the estimator without a reference clustering.
    [Fact]
    public void A_reviewed_sample_feeds_the_estimator_without_a_reference_clustering()
    {
        var clusters = new[]
        {
            new RecordCluster(["a1", "a2", "j1"]),   // j1 is a false merge
            new RecordCluster(["b1"]),
            new RecordCluster(["c1"]),
        };
        var ids = new[] { "a1", "a2", "j1", "b1", "c1" };
        var pairs = new List<PairLinkage>();
        for (var i = 0; i < ids.Length; i++)
        {
            for (var j = i + 1; j < ids.Length; j++)
            {
                pairs.Add(new PairLinkage(ids[i], ids[j], LinkageClassification.NonMatch, -5.0));
            }
        }

        var analysis = new LinkageAnalysis(new ClusteringResult(clusters, []), pairs, null);
        var sample = ClericalReviewSampler.DrawPilot(analysis, perStratum: 5, seed: 7);   // every stratum taken whole

        // The truth the reviewer knows: {a1, a2}, {j1}, {b1, c1}.
        var same = new HashSet<(string, string)> { ("a1", "a2"), ("b1", "c1") };
        var verdicts = new List<ReviewVerdict>();
        foreach (var record in ids)
        {
            foreach (var candidate in ids.Where(other => other != record))
            {
                var isSame = same.Contains((record, candidate)) || same.Contains((candidate, record));
                verdicts.Add(V(record, candidate, isSame ? ReviewOutcome.Same : ReviewOutcome.Different));
            }
        }

        var scoring = ClericalReviewScoring.Score(analysis, sample, verdicts, Reviewer);

        Assert.All(scoring.Exclusions, exclusion => Assert.Equal(0, exclusion.Excluded));
        var byId = scoring.Records.ToDictionary(r => r.RecordId);
        Assert.Equal(2.0 / 3.0, byId["a1"].Score!.Precision, 12);
        Assert.Equal(1.0 / 3.0, byId["j1"].Score!.Precision, 12);
        Assert.Equal(0.5, byId["b1"].Score!.Recall, 12);

        var precision = ClericalReviewEstimator.Estimate(scoring.Precision, seed: 7);
        var recall = ClericalReviewEstimator.Estimate(scoring.Recall, seed: 7);
        // Every stratum was a census, so the estimate is the population mean itself.
        Assert.Equal((2.0 / 3.0 + 2.0 / 3.0 + 1.0 / 3.0 + 1.0 + 1.0) / 5.0, precision.Estimate, 12);
        Assert.Equal((1.0 + 1.0 + 1.0 + 0.5 + 0.5) / 5.0, recall.Estimate, 12);
        Assert.Equal(0.0, precision.StandardError, 12);
    }

    [Fact]
    public void An_excluded_record_is_counted_in_its_stratum_and_kept_out_of_the_scores()
    {
        var clusters = new[] { new RecordCluster(["a"]), new RecordCluster(["b"]) };
        var analysis = new LinkageAnalysis(
            new ClusteringResult(clusters, []),
            [new PairLinkage("a", "b", LinkageClassification.NonMatch, -5.0)],
            null);
        var sample = ClericalReviewSampler.DrawPilot(analysis, perStratum: 5, seed: 1);

        var scoring = ClericalReviewScoring.Score(
            analysis,
            sample,
            [V("a", "b", ReviewOutcome.CannotTell), V("b", "a", ReviewOutcome.Different)],
            Reviewer);

        var singleton = Assert.Single(scoring.Exclusions);
        Assert.Equal(ReviewStratumKind.Singleton, singleton.Kind);
        Assert.Equal(2, singleton.Reviewed);
        Assert.Equal(1, singleton.Excluded);
        Assert.Equal(0.5, singleton.Rate, 12);
        Assert.Equal([1.0], Assert.Single(scoring.Precision).ReviewedScores);
    }

    [Fact]
    public void A_reviewer_with_no_verdicts_is_refused()
    {
        var clusters = new[] { new RecordCluster(["a"]) };
        var analysis = new LinkageAnalysis(new ClusteringResult(clusters, []), [], null);
        var sample = ClericalReviewSampler.DrawPilot(analysis, perStratum: 1, seed: 1);

        Assert.Throws<ArgumentException>(() => ClericalReviewScoring.Score(analysis, sample, [V("a", "b", ReviewOutcome.Same, "r2")], "r1"));
    }
}
