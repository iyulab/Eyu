namespace Eyu.Core.Linkage;

/// <summary>
/// B-cubed precision, recall and their F1 for one clustering against a reference clustering of
/// the same records, averaged per record.
/// </summary>
public sealed record ClusteringScore(double Precision, double Recall, double F1);

/// <summary>
/// Scores a clustering the way entity resolution is scored: per record, per cluster — not per
/// pair. Pairwise precision/recall weights a cluster by the square of its size, so one large
/// cluster decides the score and every small one vanishes; B-cubed (Bagga &amp; Baldwin 1998)
/// asks, for each record, how much of its predicted cluster is right and how much of its true
/// cluster it found, and averages over records — which is the unit a clerical review of a
/// sample can be scored on (Binette et al. 2024). The scoring unit is fixed here before any
/// labeled sample exists, so the sample is drawn to feed this and not the other way round.
/// </summary>
public static class ClusteringMetrics
{
    /// <summary>
    /// <paramref name="predicted"/> and <paramref name="reference"/> must partition the same set
    /// of record ids — every id in exactly one cluster on each side. A record on one side only,
    /// or twice on either side, is refused by name rather than scored as an error, because it is
    /// not a clustering mistake but a mismatch between what was clustered and what was labeled.
    /// </summary>
    public static ClusteringScore BCubed(IReadOnlyList<RecordCluster> predicted, IReadOnlyList<RecordCluster> reference)
    {
        ArgumentNullException.ThrowIfNull(predicted);
        ArgumentNullException.ThrowIfNull(reference);

        var predictedOf = ClusterOf(predicted, nameof(predicted));
        var referenceOf = ClusterOf(reference, nameof(reference));

        var onlyPredicted = predictedOf.Keys.Except(referenceOf.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var onlyReference = referenceOf.Keys.Except(predictedOf.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (onlyPredicted.Count > 0 || onlyReference.Count > 0)
        {
            throw new ArgumentException(
                "Both clusterings must cover the same record ids. "
                + (onlyPredicted.Count > 0 ? $"Only in {nameof(predicted)}: {string.Join(", ", onlyPredicted)}. " : "")
                + (onlyReference.Count > 0 ? $"Only in {nameof(reference)}: {string.Join(", ", onlyReference)}." : ""),
                nameof(reference));
        }

        if (predictedOf.Count == 0)
        {
            return new ClusteringScore(1.0, 1.0, 1.0);
        }

        double precisionSum = 0, recallSum = 0;
        foreach (var (id, predictedCluster) in predictedOf)
        {
            var referenceCluster = referenceOf[id];
            var shared = predictedCluster.Count(referenceCluster.Contains);
            precisionSum += (double)shared / predictedCluster.Count;
            recallSum += (double)shared / referenceCluster.Count;
        }

        var precision = precisionSum / predictedOf.Count;
        var recall = recallSum / predictedOf.Count;
        var f1 = precision + recall <= 0.0 ? 0.0 : 2.0 * precision * recall / (precision + recall);
        return new ClusteringScore(precision, recall, f1);
    }

    private static Dictionary<string, HashSet<string>> ClusterOf(IReadOnlyList<RecordCluster> clusters, string parameterName)
    {
        var clusterOf = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var cluster in clusters)
        {
            var members = new HashSet<string>(cluster.RecordIds, StringComparer.Ordinal);
            foreach (var id in cluster.RecordIds)
            {
                if (!clusterOf.TryAdd(id, members))
                {
                    throw new ArgumentException($"Record id '{id}' appears in more than one cluster.", parameterName);
                }
            }
        }

        return clusterOf;
    }
}
