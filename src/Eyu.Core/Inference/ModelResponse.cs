namespace Eyu.Core.Inference;

/// <summary>
/// A completion from <see cref="Eyu.Core.Ports.IModelClient"/>. <paramref name="Confidence"/>, when
/// supplied, must be treated as uncalibrated unless the implementation states otherwise — see
/// design rationale §D. It is not used to route anything on its own; a caller tunes routing
/// thresholds against observed precision/recall on held-out cases. <paramref name="Usage"/> carries
/// the provider's own token counts when it reports them, so a caller can budget context or bill
/// against the same numbers; it is <c>null</c> when the provider omits usage.
/// </summary>
public sealed record ModelResponse(string Text, double? Confidence = null, TokenUsage? Usage = null);
