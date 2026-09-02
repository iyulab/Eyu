using System.Text;
using System.Text.Json;
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
/// The response never carries a confidence score, and this type never invents one — see design
/// rationale §D: an absent confidence is honest, a fabricated one is not.
/// </summary>
public sealed class HttpModelClient(HttpClient httpClient, string model) : IModelClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(
            new ChatCompletionRequest(model, [new ChatMessage("user", request.Prompt)]),
            JsonOptions);

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var httpResponse = await httpClient.PostAsync("chat/completions", content, cancellationToken).ConfigureAwait(false);
        httpResponse.EnsureSuccessStatusCode();

        var responseBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var parsed = JsonSerializer.Deserialize<ChatCompletionResponse>(responseBody, JsonOptions);
        var messageText = parsed?.Choices is [{ Message.Content: { Length: > 0 } text }, ..] ? text : null;

        if (messageText is null)
        {
            throw new InvalidOperationException("The completion response carried no message content.");
        }

        return new ModelResponse(messageText);
    }

    private sealed record ChatCompletionRequest(string Model, IReadOnlyList<ChatMessage> Messages);

    private sealed record ChatMessage(string Role, string Content);

    private sealed record ChatCompletionResponse(IReadOnlyList<ChatChoice>? Choices);

    private sealed record ChatChoice(ChatResponseMessage? Message);

    private sealed record ChatResponseMessage(string? Content);
}
