using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Eyu.Core.Ports;

namespace Eyu.Core.Inference.Http;

/// <summary>
/// <see cref="IModelClient"/> over an OpenAI-compatible chat-completions endpoint (the format
/// shared by OpenAI itself, Azure OpenAI's compatible mode, and most self-hosted inference
/// servers) — the widest-reach provider-neutral choice, and requires nothing beyond the base
/// class library (no model SDK package). The caller owns <see cref="HttpClient.BaseAddress"/>,
/// authentication headers, and timeouts on the injected <see cref="HttpClient"/>; this type only
/// knows the request/response shape at <c>chat/completions</c>.
///
/// <paramref name="extraBody"/> lets the caller layer provider-specific request fields the library
/// does not model — a self-hosted server's thinking control (<c>chat_template_kwargs</c>,
/// <c>reasoning</c>), a <c>temperature</c> — as opaque JSON merged into every request body (the
/// OpenAI SDK's <c>extra_body</c> convention). It is deliberately untyped: those fields are the
/// provider's contract, not Eyu's, so they pass through verbatim rather than earning named slots.
/// The client owns <c>model</c> and <c>messages</c>; extra-body entries with either key are ignored.
///
/// The response never carries a confidence score, and this type never invents one — see design
/// rationale §D: an absent confidence is honest, a fabricated one is not.
/// </summary>
public sealed class HttpModelClient(HttpClient httpClient, string model, IReadOnlyDictionary<string, JsonElement>? extraBody = null) : IModelClient
{
    private const int DiagnosticExcerptLength = 500;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = JsonSerializer.SerializeToNode(new[] { new ChatMessage("user", request.Prompt) }, JsonOptions),
        };
        if (extraBody is not null)
        {
            foreach (var (key, value) in extraBody)
            {
                // The client owns model and messages; opaque extras only add sibling fields.
                if (key is "model" or "messages")
                {
                    continue;
                }

                body[key] = JsonSerializer.SerializeToNode(value, JsonOptions);
            }
        }

        var payload = body.ToJsonString(JsonOptions);

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var httpResponse = await httpClient.PostAsync("chat/completions", content, cancellationToken).ConfigureAwait(false);
        httpResponse.EnsureSuccessStatusCode();

        var responseBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var parsed = JsonSerializer.Deserialize<ChatCompletionResponse>(responseBody, JsonOptions);
        var messageText = parsed?.Choices is [{ Message.Content: { Length: > 0 } text }, ..] ? text : null;

        if (messageText is null)
        {
            throw new InvalidOperationException(
                $"The completion response carried no message content. Response body: {Excerpt(responseBody)}");
        }

        var usage = parsed?.Usage is { } u ? new TokenUsage(u.PromptTokens, u.CompletionTokens, u.TotalTokens) : null;
        return new ModelResponse(messageText, Usage: usage);
    }

    /// <summary>
    /// Bounded excerpt of what the provider actually returned. A parse failure that reports only
    /// "no content" forces the caller to re-run with logging to learn why (a refusal, a filtered
    /// completion, a differently-shaped payload) — the body is already in hand here, so the
    /// exception carries it rather than discarding it.
    /// </summary>
    private static string Excerpt(string text) => text.Length switch
    {
        0 => "(empty)",
        <= DiagnosticExcerptLength => text,
        _ => $"{text[..DiagnosticExcerptLength]}… ({text.Length} chars total)",
    };

    private sealed record ChatMessage(string Role, string Content);

    private sealed record ChatCompletionResponse(IReadOnlyList<ChatChoice>? Choices, ChatUsage? Usage);

    private sealed record ChatChoice(ChatResponseMessage? Message);

    private sealed record ChatResponseMessage(string? Content);

    private sealed record ChatUsage(
        [property: JsonPropertyName("prompt_tokens")] int? PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int? CompletionTokens,
        [property: JsonPropertyName("total_tokens")] int? TotalTokens);
}
