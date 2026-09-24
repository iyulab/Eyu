using Eyu.Core.Linkage;
using Xunit;

namespace Eyu.Core.Tests.Linkage;

/// <summary>
/// The protocol end to end on a batch of sixteen records with ten true entities — the shape a
/// consumer walked through by hand before <see cref="ClericalReviewScoring"/> existed, when the
/// only scoreable path needed a full reference clustering and a patched one quietly produced a
/// wrong estimate. Pins the number the protocol gives when followed as written: a pilot draw of
/// three per stratum, one reviewer's verdicts on the sampled records, composition, estimation.
/// </summary>
public class ClericalReviewWalkthroughTests
{
    private const string Reviewer = "r1";

    /// <summary>The seed under which the pilot draws a1, a3 and j1 from the contested stratum — the draw the walkthrough had.</summary>
    private const int Seed = 7;

    private static readonly string[] Ids =
        ["a1", "a2", "a3", "j1", "b1", "b2", "c1", "c2", "d1", "d2", "e1", "e2", "f1", "g1", "h1", "i1"];

    /// <summary>What the reviewer knows: a is three records, every other pair is two or one.</summary>
    private static readonly string[][] Truth =
        [["a1", "a2", "a3"], ["b1", "b2"], ["c1", "c2"], ["d1", "d2"], ["e1", "e2"], ["f1"], ["g1"], ["h1"], ["i1"], ["j1"]];

    private static FieldLinkageParameters Parameters() =>
        new(new Dictionary<string, double>(), new Dictionary<string, double>(), 0.5, EstimationStatus.HeuristicDefault);

    /// <summary>
    /// Nine predicted clusters over sixteen records. j1 is a namesake merged into a on ambiguous
    /// evidence (posterior 0.5), and f1 is ambiguous against j1 but left alone — so the contested
    /// stratum holds five records, clean merges eight, singletons three.
    /// </summary>
    internal static LinkageAnalysis SixteenRecordBatch()
    {
        var clusters = new[]
        {
            new RecordCluster(["a1", "a2", "a3", "j1"]),
            new RecordCluster(["b1", "b2"]),
            new RecordCluster(["c1", "c2"]),
            new RecordCluster(["d1", "d2"]),
            new RecordCluster(["e1", "e2"]),
            new RecordCluster(["f1"]),
            new RecordCluster(["g1"]),
            new RecordCluster(["h1"]),
            new RecordCluster(["i1"]),
        };
        var clusterOf = clusters.SelectMany(c => c.RecordIds.Select(id => (id, c))).ToDictionary(x => x.id, x => x.c);
        var ambiguous = new HashSet<(string, string)> { ("a1", "j1"), ("a2", "j1"), ("a3", "j1"), ("f1", "j1") };

        var pairs = new List<PairLinkage>();
        for (var i = 0; i < Ids.Length; i++)
        {
            for (var j = i + 1; j < Ids.Length; j++)
            {
                var (a, b) = (Ids[i], Ids[j]);
                if (ambiguous.Contains((a, b)) || ambiguous.Contains((b, a)))
                {
                    pairs.Add(new PairLinkage(a, b, LinkageClassification.GrayZone, 0.0));
                }
                else if (ReferenceEquals(clusterOf[a], clusterOf[b]))
                {
                    pairs.Add(new PairLinkage(a, b, LinkageClassification.Match, 8.0));
                }
                else
                {
                    pairs.Add(new PairLinkage(a, b, LinkageClassification.NonMatch, -8.0));
                }
            }
        }

        return new LinkageAnalysis(new ClusteringResult(clusters, []), pairs, Parameters());
    }

    private static bool SameEntity(string a, string b) => Truth.Any(t => t.Contains(a) && t.Contains(b));

    [Fact]
    public void The_batch_stratifies_as_the_walkthrough_reported()
    {
        var strata = ClericalReviewSampler.StratifyRecords(SixteenRecordBatch());

        Assert.Equal(5, strata.Count(s => s.Value == ReviewStratumKind.Contested));
        Assert.Equal(8, strata.Count(s => s.Value == ReviewStratumKind.CleanMerge));
        Assert.Equal(3, strata.Count(s => s.Value == ReviewStratumKind.Singleton));
    }

    // Pilot of three per stratum: the contested draw is a1, a3, j1; clean merges and singletons
    // three each (singletons taken whole). The reviewer judges every candidate of every sampled
    // record. Composition gives a1 and a3 a precision of 3/4 (j1 rejected), j1 1/4, everything
    // else 1 — and the stratified estimate lands at 5/16 · 7/12 + 8/16 + 3/16 ≈ 0.870, next to the
    // census B-cubed precision of 0.906 that the full truth would give. The walkthrough's patched
    // reference produced 0.568 for the same verdicts; that gap is the reason the scoring path
    // exists.
    [Fact]
    public void A_pilot_of_three_per_stratum_estimates_precision_at_about_0_87()
    {
        var analysis = SixteenRecordBatch();
        var sample = ClericalReviewSampler.DrawPilot(analysis, perStratum: 3, seed: Seed);

        var contested = sample.Strata.Single(s => s.Name == nameof(ReviewStratumKind.Contested));
        Assert.Equal(5, contested.PopulationSize);
        Assert.Equal(["a1", "a3", "j1"], contested.SelectedRecordIds.Order(StringComparer.Ordinal));
        Assert.Equal(3, sample.Strata.Single(s => s.Name == nameof(ReviewStratumKind.Singleton)).SelectedRecordIds.Count);
        Assert.True(sample.Caveats.HasFlag(ReviewSampleCaveat.StratumTakenWhole));

        var verdicts = sample.Strata
            .SelectMany(s => s.SelectedRecordIds)
            .SelectMany(record => Ids.Where(other => other != record)
                .Select(candidate => new ReviewVerdict(record, candidate, SameEntity(record, candidate) ? ReviewOutcome.Same : ReviewOutcome.Different, Reviewer)))
            .ToList();

        var scoring = ClericalReviewScoring.Score(analysis, sample, verdicts, Reviewer);

        Assert.All(scoring.Exclusions, exclusion => Assert.Equal(0, exclusion.Excluded));
        var byId = scoring.Records.ToDictionary(r => r.RecordId);
        Assert.Equal(0.75, byId["a1"].Score!.Precision, 12);
        Assert.Equal(0.75, byId["a3"].Score!.Precision, 12);
        Assert.Equal(0.25, byId["j1"].Score!.Precision, 12);
        Assert.Equal(["j1"], byId["a1"].FalseMergeIds);
        Assert.All(scoring.Records.Where(r => r.RecordId is not ("a1" or "a3" or "j1")), r => Assert.Equal(1.0, r.Score!.Precision, 12));
        Assert.All(scoring.Records, r => Assert.Equal(1.0, r.Score!.Recall, 12));

        var precision = ClericalReviewEstimator.Estimate(scoring.Precision, seed: Seed);
        var expected = 5.0 / 16 * ((0.75 + 0.75 + 0.25) / 3) + 8.0 / 16 * 1.0 + 3.0 / 16 * 1.0;
        Assert.Equal(expected, precision.Estimate, 12);
        Assert.Equal(0.87, precision.Estimate, 2);

        // The census the reviewer would have reached by judging everything: a1..a3 at 3/4, j1 at
        // 1/4, twelve records at 1.
        var truth = Truth.Select(t => new RecordCluster(t)).ToArray();
        var census = ClusteringMetrics.BCubedPerRecord(analysis.Clustering.Clusters, truth).Values.Average(s => s.Precision);
        Assert.Equal(14.5 / 16, census, 12);
    }
}
