using Eyu.Core.Records;

namespace Eyu.Core.Linkage;

/// <summary>
/// Orchestrates <see cref="FieldComparator"/> -&gt; <see cref="FellegiSunterEstimator"/> -&gt;
/// <see cref="LinkageClassifier"/> -&gt; <see cref="EntityClusterer"/> into one
/// <see cref="LinkageAnalysis"/> per record batch. Fewer than two records has nothing to compare,
/// so the pipeline is skipped entirely and every record becomes its own singleton cluster.
/// </summary>
public static class LinkagePipeline
{
    public static LinkageAnalysis Analyze(IReadOnlyList<RawRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var recordIds = records.Select(r => r.Id).ToList();

        if (records.Count < 2)
        {
            var singletonClusters = recordIds.Select(id => new RecordCluster([id])).ToList();
            return new LinkageAnalysis(new ClusteringResult(singletonClusters, []), []);
        }

        var pairs = new List<(RawRecord A, RawRecord B)>();
        var comparisonVectors = new List<IReadOnlyDictionary<string, FieldAgreementLevel>>();

        for (var i = 0; i < records.Count; i++)
        {
            for (var j = i + 1; j < records.Count; j++)
            {
                pairs.Add((records[i], records[j]));
                comparisonVectors.Add(FieldComparator.Compare(records[i], records[j]));
            }
        }

        var parameters = FellegiSunterEstimator.Estimate(comparisonVectors);

        var pairLinkages = new List<PairLinkage>(pairs.Count);
        for (var i = 0; i < pairs.Count; i++)
        {
            var llr = FellegiSunterEstimator.ComputeLogLikelihoodRatio(comparisonVectors[i], parameters);
            var classification = LinkageClassifier.Classify(llr);
            pairLinkages.Add(new PairLinkage(pairs[i].A.Id, pairs[i].B.Id, classification, llr));
        }

        var clustering = EntityClusterer.Cluster(recordIds, pairLinkages);
        return new LinkageAnalysis(clustering, pairLinkages);
    }
}
