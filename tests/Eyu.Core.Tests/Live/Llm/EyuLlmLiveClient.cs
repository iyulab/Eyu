using System.Net.Http.Headers;
using Eyu.Core.Inference.Http;
using Eyu.Core.Ports;

namespace Eyu.Core.Tests.Live.Llm;

/// <summary>
/// Builds the <see cref="IModelClient"/> the live suite talks to — a real
/// <see cref="HttpModelClient"/> over any OpenAI-compatible endpoint, selected entirely through
/// <c>EYU_LLM_*</c> environment variables (mirrors formbase's <c>FORMBASE_LLM_*</c> live suite one
/// repo over) so the suite never pins a provider. A single real-model round trip observed ~2 minutes
/// (thinking-model reasoning tokens precede the answer) — the default <see cref="HttpClient"/>
/// 100s timeout is too short for that and was raised here, not in <see cref="HttpModelClient"/>
/// itself, which leaves timeouts to the caller by design.
/// </summary>
internal static class EyuLlmLiveClient
{
    public static (HttpClient HttpClient, HttpModelClient ModelClient) Create()
    {
        var endpoint = Require("EYU_LLM_ENDPOINT");
        var apiKey = Require("EYU_LLM_API_KEY");
        var model = Require("EYU_LLM_MODEL");

        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(endpoint.TrimEnd('/') + "/v1/"),
            Timeout = TimeSpan.FromMinutes(5),
        };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        return (httpClient, new HttpModelClient(httpClient, model));
    }

    private static string Require(string variable)
        => Environment.GetEnvironmentVariable(variable)
            ?? throw new InvalidOperationException($"{variable} is required for the LLM live suite.");
}
