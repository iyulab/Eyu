namespace Eyu.Core.Inference;

/// <summary>A single provider-neutral inference request handed to <see cref="Eyu.Core.Ports.IModelClient"/>.</summary>
public sealed record ModelRequest(string Prompt);
