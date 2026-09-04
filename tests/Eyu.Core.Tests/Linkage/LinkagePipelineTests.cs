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

    [Fact]
    public void An_empty_or_single_record_batch_has_no_Parameters()
    {
        Assert.Null(LinkagePipeline.Analyze([]).Parameters);
        Assert.Null(LinkagePipeline.Analyze([Record("rec-1", ("name", "Acme"))]).Parameters);
    }

    [Fact]
    public void A_multi_record_batch_exposes_the_parameters_used_to_classify_its_pairs()
    {
        var records = new[]
        {
            Record("rec-1", ("name", "Acme Corp"), ("city", "Springfield")),
            Record("rec-2", ("name", "Acme Corp"), ("city", "Springfield")),
        };

        var analysis = LinkagePipeline.Analyze(records);

        Assert.NotNull(analysis.Parameters);
        Assert.Equal(FellegiSunterEstimator.HeuristicDefaultMatchPrior, analysis.Parameters!.MatchPrior);
        Assert.Equal(EstimationStatus.HeuristicDefault, analysis.Parameters.Status);
    }

    [Fact]
    public void Custom_LinkageOptions_thresholds_change_classification()
    {
        // One shared field, both agree: LLR = ln 9 ~ 2.197 -- GrayZone under the default 4.0
        // match threshold, but Match under a lowered 2.0 threshold.
        var records = new[]
        {
            Record("rec-1", ("name", "Acme")),
            Record("rec-2", ("name", "Acme")),
        };

        var defaultAnalysis = LinkagePipeline.Analyze(records);
        var loweredThresholdAnalysis = LinkagePipeline.Analyze(records, new LinkageOptions(MatchThreshold: 2.0));

        Assert.Equal(LinkageClassification.GrayZone, Assert.Single(defaultAnalysis.PairLinkages).Classification);
        Assert.Equal(LinkageClassification.Match, Assert.Single(loweredThresholdAnalysis.PairLinkages).Classification);
    }

    [Fact]
    public void Custom_LinkageOptions_EM_tuning_values_reach_the_estimator()
    {
        // 4 records, 6 pairs (>= MinimumPairsForEmEstimation) with two fields whose agreement
        // pattern is genuinely mixed, so the very first EM M-step moves m/u measurably away from
        // their 0.9/0.1 starting point -- this is what makes maxIterations:1 with the default
        // tolerance reliably NOT converge, while the same maxIterations:1 with an enormous
        // tolerance trivially DOES converge (any first-step movement clears it). Both outcomes can
        // only differ if both LinkageOptions fields actually reach FellegiSunterEstimator.Estimate.
        var records = new[]
        {
            Record("r1", ("f1", "A"), ("f2", "X")),
            Record("r2", ("f1", "A"), ("f2", "Y")),
            Record("r3", ("f1", "B"), ("f2", "X")),
            Record("r4", ("f1", "B"), ("f2", "Y")),
        };

        var notConverged = LinkagePipeline.Analyze(records, new LinkageOptions(MaxIterations: 1));
        var converged = LinkagePipeline.Analyze(records, new LinkageOptions(MaxIterations: 1, ConvergenceTolerance: 10.0));

        Assert.Equal(EstimationStatus.NotConverged, notConverged.Parameters!.Status);
        Assert.Equal(EstimationStatus.Converged, converged.Parameters!.Status);
    }
}
