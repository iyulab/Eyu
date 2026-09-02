namespace Eyu.Core.Inference;

/// <summary>
/// A completion from <see cref="Eyu.Core.Ports.IModelClient"/>. <paramref name="Confidence"/>, when
/// supplied, must be treated as uncalibrated unless the implementation states otherwise — see
/// design rationale §D. It is not used to route anything on its own; a caller tunes routing
/// thresholds against observed precision/recall on held-out cases.
/// </summary>
public sealed record ModelResponse(string Text, double? Confidence = null);
