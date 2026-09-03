namespace Eyu.Core.Linkage;

/// <summary>
/// <paramref name="Clusters"/> covers every input record exactly once (singletons included).
/// <paramref name="GrayZonePairs"/> lists only the ambiguous pairs still unresolved after
/// Match-based clustering — a gray-zone pair whose two records already ended up in the same
/// cluster via a chain of Match edges is not repeated here, there is nothing left to adjudicate.
/// </summary>
public sealed record ClusteringResult(IReadOnlyList<RecordCluster> Clusters, IReadOnlyList<PairLinkage> GrayZonePairs);
