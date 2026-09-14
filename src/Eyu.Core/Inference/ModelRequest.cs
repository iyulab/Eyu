using System.Text.Json;

namespace Eyu.Core.Inference;

/// <summary>A single provider-neutral inference request handed to <see cref="Eyu.Core.Ports.IModelClient"/>.</summary>
/// <param name="Prompt">The text the model answers.</param>
/// <param name="ResponseSchema">
/// A JSON Schema the response text is expected to satisfy, when the caller knows the shape it will
/// parse — or <see langword="null"/> for free text. An implementation maps it to its provider's
/// structured-output feature where one exists, which keeps a model from wrapping its answer in
/// prose or a markdown fence. It is a request, not a guarantee: an implementation may ignore it, so
/// a caller still states the shape in <see cref="Prompt"/> and still validates what comes back.
/// Keep the schema to the strict subset — every property required, no additional properties — so
/// that providers enforcing strict schemas accept it.
/// </param>
public sealed record ModelRequest(string Prompt, JsonElement? ResponseSchema = null);
