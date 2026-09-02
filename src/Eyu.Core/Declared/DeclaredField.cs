namespace Eyu.Core.Declared;

/// <summary>
/// A single field a caller has already declared on a subject. <paramref name="SemanticHint"/> is
/// an optional caller-supplied gloss (e.g. "monetary amount, minor units") — declared structure
/// always wins over inference, so a hint here narrows judgment rather than replacing it.
/// </summary>
public sealed record DeclaredField(string Name, string? SemanticHint = null);
