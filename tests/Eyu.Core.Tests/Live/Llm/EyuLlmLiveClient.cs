using System.Net.Http.Headers;
using Eyu.Core.Inference.Http;
using Eyu.Core.Ports;
using Eyu.Core.Tests.Quality;

namespace Eyu.Core.Tests.Live.Llm;

/// <summary>
/// Builds the <see cref="IModelClient"/> the live suite talks to — a real
/// <see cref="HttpModelClient"/> over any OpenAI-compatible endpoint, selected entirely through
/// <c>EYU_LLM_*</c> environment variables (mirrors formbase's <c>FORMBASE_LLM_*</c> live suite one
/// repo over) so the suite never pins a provider. A single real-model round trip observed ~2 minutes
/// (thinking-model reasoning tokens precede the answer) — the default <see cref="HttpClient"/>
/// 100s timeout is too short for that and was raised here, not in <see cref="HttpModelClient"/>
/// itself, which leaves timeouts to the caller by design.
/// <para>
/// <c>EYU_LLM_RAW_RESPONSE_DIR</c> keeps every call's request and response there
/// (<see cref="RecordingModelClient"/>). <c>EYU_LLM_REPLAY_DIR</c> answers from such a directory instead
/// of a model (<see cref="ReplayModelClient"/>) — no endpoint is needed then, and there is no
/// <see cref="HttpClient"/> to dispose.
/// </para>
/// </summary>
internal static class EyuLlmLiveClient
{
    public static (HttpClient? HttpClient, IModelClient ModelClient) Create()
    {
        if (Environment.GetEnvironmentVariable("EYU_LLM_REPLAY_DIR") is { Length: > 0 } replay)
        {
            return (null, new ReplayModelClient(replay));
        }

        var endpoint = Require("EYU_LLM_ENDPOINT");
        var apiKey = Require("EYU_LLM_API_KEY");
        var model = Require("EYU_LLM_MODEL");

        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(endpoint.TrimEnd('/') + "/v1/"),
            // EYU_LLM_TIMEOUT_MINUTES raises it for a call that is large on purpose — every source of a
            // cross-source case in one prompt outlasts five minutes of reasoning on a shared server.
            Timeout = TimeSpan.FromMinutes(
                int.TryParse(Environment.GetEnvironmentVariable("EYU_LLM_TIMEOUT_MINUTES"), out var minutes) && minutes > 0 ? minutes : 5),
        };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        IModelClient client = new HttpModelClient(httpClient, model);
        if (Environment.GetEnvironmentVariable("EYU_LLM_RAW_RESPONSE_DIR") is { Length: > 0 } raw)
        {
            client = new RecordingModelClient(client, raw);
        }

        return (httpClient, client);
    }

    private static string Require(string variable)
        => Environment.GetEnvironmentVariable(variable)
            ?? throw new InvalidOperationException($"{variable} is required for the LLM live suite.");
}
