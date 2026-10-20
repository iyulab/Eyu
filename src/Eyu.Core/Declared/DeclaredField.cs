namespace Eyu.Core.Declared;

/// <summary>
/// A single field a caller has already declared on a subject. Everything beyond
/// <paramref name="Name"/> is optional, because a declaration may know only the name — but when a
/// caller does know the value <paramref name="Kind"/> or that the field is
/// <paramref name="Required"/>, that is declared fact, and declared structure always wins over
/// inference: a model is told it rather than left to guess it from a sample.
/// <paramref name="SemanticHint"/> is a caller-supplied gloss (e.g. "monetary amount, minor units")
/// that narrows judgment rather than replacing it. <paramref name="BusinessName"/> is the name a
/// person reads the field by («로트 번호» for <c>lot_no</c> — ODCS's <c>businessName</c>): declared, so
/// the model is shown it beside the machine name instead of guessing what an abbreviation means. It is
/// the caller's own name for the field, never a synonym Eyu infers.
/// </summary>
public sealed record DeclaredField(
    string Name,
    string? SemanticHint = null,
    DeclaredValueKind? Kind = null,
    bool? Required = null,
    string? BusinessName = null);
