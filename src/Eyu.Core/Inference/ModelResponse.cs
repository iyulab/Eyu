namespace Eyu.Core.Inference;

/// <summary>
/// A completion from <see cref="Eyu.Core.Ports.IModelClient"/>. <paramref name="Confidence"/>, when
/// supplied, must be treated as uncalibrated unless the implementation states otherwise — see
/// design rationale §D. It is not used to route anything on its own; a caller tunes routing
/// thresholds against observed precision/recall on held-out cases. <paramref name="Usage"/> carries
/// the provider's own token counts when it reports them, so a caller can budget context or bill
/// against the same numbers; it is <c>null</c> when the provider omits usage. <paramref name="FinishReason"/> is the
/// provider's own reason the model stopped (<c>stop</c>, <c>length</c> — it ran out of output —, <c>content_filter</c>, …) when it reports one,
/// so a caller can tell an answer that was cut off from one that was finished and wrong; it is <c>null</c> when the provider omits it.
/// </summary>
public sealed record ModelResponse(string Text, double? Confidence = null, TokenUsage? Usage = null, string? FinishReason = null);
