using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Eyu.Core.Inference;
using Eyu.Core.Ports;

namespace Eyu.Core.Tests.Quality;

/// <summary>
/// One model call as a live round answered it: the request it was asked (prompt and response schema)
/// and the response it got. Kept so a change to what Eyu does <em>after</em> the model answers — the
/// parse, the merge, linkage, the denotation check — can be run again over a past round's answers
/// instead of only over a new round, whose answers have drifted with the model.
/// </summary>
/// <param name="Key">The request's identity: SHA-256 over the prompt and the schema.</param>
/// <param name="Occurrence">Which time this round asked this exact request, from 0 — a measurement repeats each case.</param>
internal sealed record RecordedModelCall(
    string Key,
    int Occurrence,
    DateTimeOffset RecordedAt,
    string Prompt,
    JsonElement? ResponseSchema,
    string Text,
    double? Confidence,
    TokenUsage? Usage);

/// <summary>Keeps every call it passes through in <paramref name="directory"/>, one file per call.</summary>
internal sealed class RecordingModelClient(IModelClient inner, string directory) : IModelClient
{
    private readonly Dictionary<string, int> occurrences = new(StringComparer.Ordinal);

    public async Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default)
    {
        var response = await inner.CompleteAsync(request, cancellationToken);

        var key = RecordedModelResponses.KeyOf(request);
        int occurrence;
        lock (occurrences)
        {
            occurrence = occurrences.GetValueOrDefault(key);
            occurrences[key] = occurrence + 1;
        }

        var call = new RecordedModelCall(key, occurrence, DateTimeOffset.UtcNow, request.Prompt, request.ResponseSchema, response.Text, response.Confidence, response.Usage);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, $"{key[..16]}-{occurrence:D3}.json"),
            JsonSerializer.Serialize(call, RecordedModelResponses.Json),
            cancellationToken);
        return response;
    }
}

/// <summary>
/// Answers from a recorded round instead of a model: each request gets the response recorded for that
/// exact request, in the order the round got them. A request the round never asked — a prompt that has
/// changed since — is refused rather than answered with something else: replay measures what follows the
/// model's answer, and a different prompt is a question for a live round.
/// </summary>
internal sealed class ReplayModelClient : IModelClient
{
    private readonly Dictionary<string, Queue<RecordedModelCall>> calls;

    public ReplayModelClient(string directory)
    {
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"No recorded round at '{directory}'.");
        }

        calls = Directory.EnumerateFiles(directory, "*.json")
            .Select(path => JsonSerializer.Deserialize<RecordedModelCall>(File.ReadAllText(path), RecordedModelResponses.Json)
                ?? throw new InvalidDataException($"'{path}' holds no recorded call."))
            .GroupBy(call => call.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => new Queue<RecordedModelCall>(group.OrderBy(call => call.Occurrence)), StringComparer.Ordinal);
    }

    public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default)
    {
        var key = RecordedModelResponses.KeyOf(request);
        lock (calls)
        {
            if (!calls.TryGetValue(key, out var queue) || !queue.TryDequeue(out var call))
            {
                throw new InvalidOperationException(
                    $"The recorded round holds no {(queue is null ? "" : "further ")}response to this request (key {key[..16]}) — the prompt differs from the recorded one, or this round asked it fewer times.");
            }

            return Task.FromResult(new ModelResponse(call.Text, call.Confidence, call.Usage));
        }
    }
}

internal static class RecordedModelResponses
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string KeyOf(ModelRequest request)
    {
        var text = request.Prompt + "\n" + (request.ResponseSchema is { } schema ? schema.GetRawText() : string.Empty);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
