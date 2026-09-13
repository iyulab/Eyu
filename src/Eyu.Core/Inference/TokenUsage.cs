namespace Eyu.Core.Inference;

/// <summary>
/// Token counts a provider optionally reports on a completion (the OpenAI-compatible
/// <c>usage</c> object). Each field is <c>null</c> when the provider omits it — the counts are
/// passed through verbatim, never inferred, so a consumer can budget context or bill against the
/// same numbers the server returned. Not every OpenAI-compatible server reports usage.
/// </summary>
public sealed record TokenUsage(int? PromptTokens, int? CompletionTokens, int? TotalTokens);
