namespace Eyu.Core.Linkage;

/// <summary>
/// Groups records into clusters using only <see cref="LinkageClassification.Match"/> edges
/// (union-find), then reports which <see cref="LinkageClassification.GrayZone"/> pairs still
/// cross a cluster boundary — those are exactly the pairs <c>SinglePassOntologyProposer</c> asks
/// the LLM to adjudicate. <see cref="LinkageClassification.NonMatch"/> pairs need no action: not
/// unioning them is the entire effect.
/// </summary>
public static class EntityClusterer
{
    public static ClusteringResult Cluster(IReadOnlyList<string> recordIds, IReadOnlyList<PairLinkage> pairLinkages)
    {
        ArgumentNullException.ThrowIfNull(recordIds);
        ArgumentNullException.ThrowIfNull(pairLinkages);

        var parent = recordIds.ToDictionary(id => id, id => id, StringComparer.Ordinal);

        string Find(string id)
        {
            while (!string.Equals(parent[id], id, StringComparison.Ordinal))
            {
                parent[id] = parent[parent[id]];
                id = parent[id];
            }

            return id;
        }

        void Union(string a, string b)
        {
            var rootA = Find(a);
            var rootB = Find(b);
            if (!string.Equals(rootA, rootB, StringComparison.Ordinal))
            {
                parent[rootA] = rootB;
            }
        }

        foreach (var pair in pairLinkages.Where(p => p.Classification == LinkageClassification.Match))
        {
            Union(pair.RecordIdA, pair.RecordIdB);
        }

        var clusters = recordIds
            .GroupBy(Find, StringComparer.Ordinal)
            .Select(g => new RecordCluster(g.ToList()))
            .ToList();

        var grayZonePairs = pairLinkages
            .Where(p => p.Classification == LinkageClassification.GrayZone)
            .Where(p => !string.Equals(Find(p.RecordIdA), Find(p.RecordIdB), StringComparison.Ordinal))
            .ToList();

        return new ClusteringResult(clusters, grayZonePairs);
    }
}
