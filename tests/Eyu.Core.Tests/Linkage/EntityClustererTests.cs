using Eyu.Core.Linkage;
using Xunit;

namespace Eyu.Core.Tests.Linkage;

public class EntityClustererTests
{
    private static readonly string[] AandB = ["a", "b"];
    private static readonly string[] AandBandC = ["a", "b", "c"];

    [Fact]
    public void Two_records_linked_by_Match_end_up_in_one_cluster()
    {
        var pairLinkages = new[] { new PairLinkage("a", "b", LinkageClassification.Match, 5.0) };

        var result = EntityClusterer.Cluster(["a", "b"], pairLinkages);

        var cluster = Assert.Single(result.Clusters);
        Assert.Equal(AandB, cluster.RecordIds.OrderBy(id => id));
        Assert.Empty(result.GrayZonePairs);
    }

    [Fact]
    public void Two_records_linked_by_NonMatch_stay_in_separate_singleton_clusters()
    {
        var pairLinkages = new[] { new PairLinkage("a", "b", LinkageClassification.NonMatch, -5.0) };

        var result = EntityClusterer.Cluster(["a", "b"], pairLinkages);

        Assert.Equal(2, result.Clusters.Count);
        Assert.All(result.Clusters, c => Assert.Single(c.RecordIds));
        Assert.Empty(result.GrayZonePairs);
    }

    [Fact]
    public void Two_records_linked_by_GrayZone_stay_separate_and_are_reported_as_ambiguous()
    {
        var pairLinkages = new[] { new PairLinkage("a", "b", LinkageClassification.GrayZone, 0.0) };

        var result = EntityClusterer.Cluster(["a", "b"], pairLinkages);

        Assert.Equal(2, result.Clusters.Count);
        var grayPair = Assert.Single(result.GrayZonePairs);
        Assert.Equal(("a", "b"), (grayPair.RecordIdA, grayPair.RecordIdB));
    }

    [Fact]
    public void A_GrayZone_pair_reaching_outside_its_Match_cluster_is_still_reported_as_ambiguous()
    {
        // a-b Match, b-c GrayZone: once a and b are merged, does c also need adjudicating against
        // that merged cluster? Here b-c is still genuinely ambiguous (c was never linked to the
        // cluster by a Match edge), so it must still be reported.
        var pairLinkages = new[]
        {
            new PairLinkage("a", "b", LinkageClassification.Match, 5.0),
            new PairLinkage("b", "c", LinkageClassification.GrayZone, 0.0),
        };

        var result = EntityClusterer.Cluster(["a", "b", "c"], pairLinkages);

        var cluster = Assert.Single(result.Clusters, c => c.RecordIds.Count == 2);
        Assert.Equal(AandB, cluster.RecordIds.OrderBy(id => id));
        var grayPair = Assert.Single(result.GrayZonePairs);
        Assert.Equal(("b", "c"), (grayPair.RecordIdA, grayPair.RecordIdB));
    }

    [Fact]
    public void A_GrayZone_pair_whose_endpoints_are_already_in_the_same_cluster_is_not_reported()
    {
        // a-b Match, b-c Match (so a, b, c are already one cluster via chaining), and a-c happens
        // to also read GrayZone on its own -- nothing left to adjudicate, they're already merged.
        var pairLinkages = new[]
        {
            new PairLinkage("a", "b", LinkageClassification.Match, 5.0),
            new PairLinkage("b", "c", LinkageClassification.Match, 5.0),
            new PairLinkage("a", "c", LinkageClassification.GrayZone, 0.0),
        };

        var result = EntityClusterer.Cluster(["a", "b", "c"], pairLinkages);

        var cluster = Assert.Single(result.Clusters);
        Assert.Equal(AandBandC, cluster.RecordIds.OrderBy(id => id));
        Assert.Empty(result.GrayZonePairs);
    }

    [Fact]
    public void A_record_with_no_pairs_at_all_is_its_own_singleton_cluster()
    {
        var result = EntityClusterer.Cluster(["a"], []);

        var cluster = Assert.Single(result.Clusters);
        Assert.Equal(["a"], cluster.RecordIds);
    }
}
