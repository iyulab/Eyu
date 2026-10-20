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
/// knows the request/response shape at <c>chat/completions</c>, which it posts to relative to that
/// base address — so the base ends with the API version segment and a slash
/// (<c>https://host/v1/</c>): without the slash the last segment is replaced, and without the
/// version the path does not exist on most servers. A thinking model can take minutes per call, past
/// <see cref="HttpClient"/>'s default 100-second timeout.
///
/// A response outside the success range throws <see cref="HttpRequestException"/> carrying the
/// status, the request URI (without its query) and an excerpt of the body the server answered —
/// OpenAI-compatible servers state the cause there (an unknown model, a rejected field).
///
/// <paramref name="extraBody"/> lets the caller layer provider-specific request fields the library
/// does not model — a self-hosted server's thinking control (<c>chat_template_kwargs</c>,
/// <c>reasoning</c>), a <c>temperature</c> — as opaque JSON merged into every request body (the
/// OpenAI SDK's <c>extra_body</c> convention). It is deliberately untyped: those fields are the
/// provider's contract, not Eyu's, so they pass through verbatim rather than earning named slots.
/// The client owns <c>model</c> and <c>messages</c>; extra-body entries with either key are ignored.
///
/// A request carrying <see cref="ModelRequest.ResponseSchema"/> is sent with the OpenAI structured
/// output field, <c>response_format: {"type": "json_schema", "json_schema": {"name", "strict": true,
/// "schema"}}</c>; a request without one sends no <c>response_format</c>. Servers differ in what they
/// honour, so an extra-body <c>response_format</c> replaces the mapped one — the caller knows its
/// server, and <c>{"type": "text"}</c> turns structured output off for one that rejects it.
///
/// With <paramref name="stream"/> the request asks for the answer as it is written (<c>"stream": true</c>,
/// with <c>stream_options.include_usage</c>) and the client reads the server-sent events, joining each
/// chunk's <c>delta.content</c> until <c>data: [DONE]</c>. Use it behind a proxy or gateway that closes a
/// response whose first byte is slow — commonly 100 seconds — when a thinking model reasons for longer
/// than that before it answers: streamed, bytes flow while it thinks. Off by default, as in the
/// OpenAI SDKs. The client owns <c>stream</c> and <c>stream_options</c> then; extra-body entries with
/// either key are ignored. A streamed call fails the way a non-streamed one does: an error the endpoint
/// reports inside the stream, or a connection that closes before <c>[DONE]</c>, throws
/// <see cref="HttpRequestException"/>.
///
/// The response never carries a confidence score, and this type never invents one — see design
/// rationale §D: an absent confidence is honest, a fabricated one is not.
/// </summary>
public sealed partial class HttpModelClient(HttpClient httpClient, string model, IReadOnlyDictionary<string, JsonElement>? extraBody = null, bool stream = false) : IModelClient
{
    private const int DiagnosticExcerptLength = 500;

    // The structured-output field requires a name; the request carries only the schema, so the one
    // name every request shares is enough.
    private const string ResponseSchemaName = "response";

    public async Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = request.Prompt }),
        };
        if (request.ResponseSchema is { } schema)
        {
            body["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject
                {
                    ["name"] = ResponseSchemaName,
                    ["strict"] = true,
                    ["schema"] = JsonSerializer.SerializeToNode(schema, WireJsonContext.Default.JsonElement),
                },
            };
        }

        if (extraBody is not null)
        {
            foreach (var (key, value) in extraBody)
            {
                // The client owns model and messages; opaque extras add sibling fields, and replace
                // the mapped response_format when they carry one.
                if (key is "model" or "messages" || (stream && key is "stream" or "stream_options"))
                {
                    continue;
                }

                body[key] = JsonSerializer.SerializeToNode(value, WireJsonContext.Default.JsonElement);
            }
        }

        if (stream)
        {
            body["stream"] = true;
            body["stream_options"] = new JsonObject { ["include_usage"] = true };
        }

        var payload = body.ToJsonString();

        // Sent as a message this method holds, so a failure can name where it went: HttpClient
        // resolves the relative path against its base address onto this request before sending.
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        using var httpResponse = await httpClient.SendAsync(
            httpRequest,
            stream ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead,
            cancellationToken).ConfigureAwait(false);
        if (!httpResponse.IsSuccessStatusCode)
        {
            var failureBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(DescribeFailure(httpRequest, httpResponse, failureBody), inner: null, httpResponse.StatusCode);
        }

        if (stream)
        {
            return await ReadStreamAsync(httpRequest, httpResponse, cancellationToken).ConfigureAwait(false);
        }

        var responseBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var parsed = JsonSerializer.Deserialize(responseBody, WireJsonContext.Default.ChatCompletionResponse);
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
    /// Joins a streamed answer: each <c>data:</c> line is a chunk whose first choice carries a piece of
    /// the content in <c>delta.content</c> (a thinking model's <c>delta.reasoning_content</c> is not the
    /// answer and is left out), the last one before <c>[DONE]</c> may carry the usage, and a chunk that
    /// carries an <c>error</c> ends the call with it. Lines that are not <c>data:</c> — comments,
    /// keep-alives, event names — are skipped, as the server-sent-events format says. A connection that
    /// closes while the answer is still coming is a failed request too, as it is when a non-streamed
    /// body is cut short.
    /// </summary>
    private static async Task<ModelResponse> ReadStreamAsync(HttpRequestMessage httpRequest, HttpResponseMessage httpResponse, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        var seen = new StringBuilder();
        TokenUsage? usage = null;
        var done = false;
        try
        {
            await using var content = await httpResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(content, Encoding.UTF8);
            while (!done && await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                done = ReadChunk(line, text, seen, ref usage);
            }
        }
        catch (IOException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Without this the cut shows as an IOException, which a caller handling failed requests
            // does not expect from a call whose non-streamed form reports the same cut as one.
            var error = ex is HttpIOException http ? http.HttpRequestError : HttpRequestError.Unknown;
            throw new HttpRequestException(
                error,
                $"The connection to {DescribeTarget(httpRequest)} closed while the answer was streaming, after {text.Length} chars of content and before [DONE]: {ex.Message}",
                ex);
        }

        if (text.Length == 0)
        {
            throw new InvalidOperationException(
                $"The streamed completion carried no message content{(done ? "" : " and ended before [DONE]")}. Stream began: {Excerpt(seen.ToString())}");
        }

        return new ModelResponse(text.ToString(), Usage: usage);
    }

    /// <summary>
    /// Takes one line of the stream into the answer; true when it is the closing <c>[DONE]</c>.
    /// </summary>
    private static bool ReadChunk(string line, StringBuilder text, StringBuilder seen, ref TokenUsage? usage)
    {
        if (!line.StartsWith("data:", StringComparison.Ordinal))
        {
            return false;
        }

        var data = line["data:".Length..].Trim();
        if (data == "[DONE]")
        {
            return true;
        }

        if (seen.Length < DiagnosticExcerptLength)
        {
            seen.AppendLine(data);
        }

        var chunk = JsonSerializer.Deserialize(data, WireJsonContext.Default.ChatCompletionChunk);
        if (chunk?.Error is { } error)
        {
            // The endpoint failed the request after its 200 — the same failure a non-success status
            // reports, so a caller handles it the same way (a retry, a call counted as failed).
            throw new HttpRequestException($"The model endpoint reported an error in the stream: {Excerpt(error.GetRawText())}");
        }

        if (chunk?.Choices is [{ Delta.Content: { } piece }, ..])
        {
            text.Append(piece);
        }

        if (chunk?.Usage is { } u)
        {
            usage = new TokenUsage(u.PromptTokens, u.CompletionTokens, u.TotalTokens);
        }

        return false;
    }

    /// <summary>
    /// What a failed call needs to be diagnosed without re-running it: where the request went (the
    /// relative path resolved against a base address the caller built) and what the server said
    /// about it. The query is left out — some providers carry a key there.
    /// </summary>
    private static string DescribeFailure(HttpRequestMessage request, HttpResponseMessage response, string body)
    {
        var status = (int)response.StatusCode;
        var message = $"The model endpoint answered {status} {response.ReasonPhrase} to {DescribeTarget(request)}. Response body: {Excerpt(body)}";
        return response.StatusCode == System.Net.HttpStatusCode.NotFound
            ? message + " The path is resolved against HttpClient.BaseAddress, which for an OpenAI-compatible server usually ends with the API version segment and a slash (https://host/v1/)."
            : message;
    }

    /// <summary>
    /// The method and the path a request went to, without the query — some providers carry a key there.
    /// </summary>
    private static string DescribeTarget(HttpRequestMessage request)
    {
        var uri = request.RequestUri is { IsAbsoluteUri: true } requested
            ? requested.GetLeftPart(UriPartial.Path)
            : request.RequestUri?.OriginalString ?? "(unknown)";
        return $"{request.Method.Method} {uri}";
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

    private sealed record ChatCompletionResponse(IReadOnlyList<ChatChoice>? Choices, ChatUsage? Usage);

    private sealed record ChatChoice(ChatResponseMessage? Message);

    private sealed record ChatResponseMessage(string? Content);

    private sealed record ChatCompletionChunk(IReadOnlyList<ChatChunkChoice>? Choices, ChatUsage? Usage, JsonElement? Error);

    private sealed record ChatChunkChoice(ChatResponseMessage? Delta);

    private sealed record ChatUsage(
        [property: JsonPropertyName("prompt_tokens")] int? PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int? CompletionTokens,
        [property: JsonPropertyName("total_tokens")] int? TotalTokens);

    /// <summary>
    /// Serialization metadata generated at compile time rather than reflected at run time, so the
    /// client works where reflection-based serialization is off (Native AOT, trimmed hosts).
    /// </summary>
    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
    [JsonSerializable(typeof(ChatCompletionResponse))]
    [JsonSerializable(typeof(ChatCompletionChunk))]
    [JsonSerializable(typeof(JsonElement))]
    private sealed partial class WireJsonContext : JsonSerializerContext;
}
