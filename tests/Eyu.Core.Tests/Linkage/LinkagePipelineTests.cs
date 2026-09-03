using Eyu.Core.Linkage;
using Eyu.Core.Records;
using Xunit;

namespace Eyu.Core.Tests.Linkage;

public class LinkagePipelineTests
{
    private static RawRecord Record(string id, params (string Key, string? Value)[] fields) =>
        new(id, fields.ToDictionary(f => f.Key, f => f.Value));

    [Fact]
    public void An_empty_batch_produces_no_clusters_and_no_pair_linkages()
    {
        var analysis = LinkagePipeline.Analyze([]);

        Assert.Empty(analysis.Clustering.Clusters);
        Assert.Empty(analysis.PairLinkages);
    }

    [Fact]
    public void A_single_record_batch_produces_one_singleton_cluster_and_no_pair_linkages()
    {
        var analysis = LinkagePipeline.Analyze([Record("rec-1", ("name", "Acme"))]);

        var cluster = Assert.Single(analysis.Clustering.Clusters);
        Assert.Equal(["rec-1"], cluster.RecordIds);
        Assert.Empty(analysis.PairLinkages);
    }

    [Fact]
    public void Two_records_agreeing_on_every_field_produce_one_pair_linkage_and_are_classified()
    {
        var records = new[]
        {
            Record("rec-1", ("name", "Acme Corp"), ("city", "Springfield")),
            Record("rec-2", ("name", "Acme Corp"), ("city", "Springfield")),
        };

        var analysis = LinkagePipeline.Analyze(records);

        var pairLinkage = Assert.Single(analysis.PairLinkages);
        Assert.Equal(("rec-1", "rec-2"), (pairLinkage.RecordIdA, pairLinkage.RecordIdB));
        Assert.Equal(LinkageClassification.Match, pairLinkage.Classification);
    }

    [Fact]
    public void Three_records_produce_three_pair_linkages_one_per_combination()
    {
        var records = new[]
        {
            Record("rec-1", ("name", "Acme")),
            Record("rec-2", ("name", "Beta")),
            Record("rec-3", ("name", "Gamma")),
        };

        var analysis = LinkagePipeline.Analyze(records);

        Assert.Equal(3, analysis.PairLinkages.Count);
    }
}
