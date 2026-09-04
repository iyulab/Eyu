namespace Eyu.Core.Linkage;

/// <summary>
/// The full Fellegi-Sunter pre-filter result for one <c>ProposeAsync</c> call's record batch.
/// <paramref name="Parameters"/> is the EM (or heuristic-fallback) parameters that produced every
/// <see cref="PairLinkage"/> in <paramref name="PairLinkages"/> — exposed so a caller inspecting
/// this directly (e.g. a live validation harness) can see the match prior and whether EM actually
/// converged (<see cref="EstimationStatus"/>). Null only when the record batch had fewer than two
/// records, so there was nothing to compare.
/// </summary>
public sealed record LinkageAnalysis(
    ClusteringResult Clustering,
    IReadOnlyList<PairLinkage> PairLinkages,
    FieldLinkageParameters? Parameters);
