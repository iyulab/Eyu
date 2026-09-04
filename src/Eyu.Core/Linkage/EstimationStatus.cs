namespace Eyu.Core.Linkage;

/// <summary>
/// Whether <see cref="FellegiSunterEstimator.Estimate"/>'s EM loop actually converged, ran out
/// of iterations without converging, or was skipped entirely in favor of the heuristic default
/// (too few comparison pairs — see <see cref="FellegiSunterEstimator.MinimumPairsForEmEstimation"/>).
/// Exposed via <see cref="FieldLinkageParameters.Status"/> so a caller inspecting
/// <see cref="LinkageAnalysis.Parameters"/> directly (e.g. a live validation harness) can tell
/// which regime produced a given batch's parameters, without Eyu.Core needing a logging port.
/// </summary>
public enum EstimationStatus
{
    Converged,
    NotConverged,
    HeuristicDefault,
}
