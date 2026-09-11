using Eyu.Core.Linkage;
using Xunit;

namespace Eyu.Core.Tests.Linkage;

public class ClusteringMetricsTests
{
    private static RecordCluster C(params string[] ids) => new(ids);

    [Fact]
    public void The_same_clustering_scores_perfectly()
    {
        var clustering = new[] { C("a", "b"), C("c"), C("d", "e", "f") };

        var score = ClusteringMetrics.BCubed(clustering, clustering);

        Assert.Equal(1.0, score.Precision, 12);
        Assert.Equal(1.0, score.Recall, 12);
        Assert.Equal(1.0, score.F1, 12);
    }

    [Fact]
    public void Splitting_everything_into_singletons_is_precise_and_misses_every_link()
    {
        // Four records that truly form one entity, predicted as four singletons: each predicted
        // cluster is entirely right (precision 1), and each record found 1 of its 4 true
        // co-members (recall 0.25).
        var predicted = new[] { C("a"), C("b"), C("c"), C("d") };
        var reference = new[] { C("a", "b", "c", "d") };

        var score = ClusteringMetrics.BCubed(predicted, reference);

        Assert.Equal(1.0, score.Precision, 12);
        Assert.Equal(0.25, score.Recall, 12);
        Assert.Equal(0.4, score.F1, 12);
    }

    [Fact]
    public void Merging_everything_into_one_cluster_finds_every_link_and_is_mostly_wrong()
    {
        var predicted = new[] { C("a", "b", "c", "d") };
        var reference = new[] { C("a", "b"), C("c"), C("d") };

        var score = ClusteringMetrics.BCubed(predicted, reference);

        // a and b: 2 of 4 co-members right; c and d: 1 of 4 -> (0.5 + 0.5 + 0.25 + 0.25) / 4.
        Assert.Equal(0.375, score.Precision, 12);
        Assert.Equal(1.0, score.Recall, 12);
    }

    [Fact]
    public void A_record_is_scored_by_its_own_cluster_so_small_clusters_are_not_drowned_by_a_large_one()
    {
        // One big cluster of 6 predicted correctly, plus three pairs each wrongly split into
        // singletons. Pairwise scoring would count 15 correct pairs against 3 missed and read
        // ~0.83 recall; per record, 6 of 12 records have recall 1 and 6 have recall 0.5.
        var predicted = new[] { C("a", "b", "c", "d", "e", "f"), C("p"), C("q"), C("r"), C("s"), C("t"), C("u") };
        var reference = new[] { C("a", "b", "c", "d", "e", "f"), C("p", "q"), C("r", "s"), C("t", "u") };

        var score = ClusteringMetrics.BCubed(predicted, reference);

        Assert.Equal(1.0, score.Precision, 12);
        Assert.Equal(0.75, score.Recall, 12);
    }

    [Fact]
    public void Two_clusterings_of_different_records_are_refused_by_name()
    {
        var predicted = new[] { C("a", "b"), C("c") };
        var reference = new[] { C("a", "b"), C("x") };

        var error = Assert.Throws<ArgumentException>(() => ClusteringMetrics.BCubed(predicted, reference));

        Assert.Contains("Only in predicted: c", error.Message, StringComparison.Ordinal);
        Assert.Contains("Only in reference: x", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_record_in_two_clusters_on_one_side_is_refused()
    {
        var predicted = new[] { C("a", "b"), C("b", "c") };
        var reference = new[] { C("a", "b", "c") };

        var error = Assert.Throws<ArgumentException>(() => ClusteringMetrics.BCubed(predicted, reference));

        Assert.Contains("'b'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_to_score_is_a_perfect_score_not_a_division_by_zero()
    {
        var score = ClusteringMetrics.BCubed([], []);

        Assert.Equal(new ClusteringScore(1.0, 1.0, 1.0), score);
    }

    [Fact]
    public void The_pipelines_own_clustering_scores_itself_perfectly()
    {
        var analysis = LinkagePipeline.Analyze(
        [
            new Eyu.Core.Records.RawRecord("r1", new Dictionary<string, string?> { ["name"] = "Alpha" }),
            new Eyu.Core.Records.RawRecord("r2", new Dictionary<string, string?> { ["name"] = "Alpha" }),
            new Eyu.Core.Records.RawRecord("r3", new Dictionary<string, string?> { ["name"] = "Beta" }),
        ]);

        var score = ClusteringMetrics.BCubed(analysis.Clustering.Clusters, analysis.Clustering.Clusters);

        Assert.Equal(1.0, score.F1, 12);
    }
}
