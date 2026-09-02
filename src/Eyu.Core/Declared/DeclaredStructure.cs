using Eyu.Core.Primitives;

namespace Eyu.Core.Declared;

/// <summary>
/// What a caller has already declared for <paramref name="Subject"/> — field hints, relations, and
/// an opaque <paramref name="Version"/> a consumer may use to detect redeclaration. Declared
/// structure always wins over inference (README: "Declared always wins").
/// </summary>
public sealed record DeclaredStructure(
    SubjectRef Subject,
    IReadOnlyList<DeclaredField> Fields,
    IReadOnlyList<DeclaredRelation> Relations,
    string? Version = null);
