namespace Eyu.Core.Linkage;

/// <summary>The full Fellegi-Sunter pre-filter result for one <c>ProposeAsync</c> call's record batch.</summary>
public sealed record LinkageAnalysis(ClusteringResult Clustering, IReadOnlyList<PairLinkage> PairLinkages);
