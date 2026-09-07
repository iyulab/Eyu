using Eyu.Core.Primitives;

namespace Eyu.Core.Declared;

/// <summary>
/// A caller-declared link from one subject to another. <paramref name="ViaField"/> names the field
/// that carries the relation's key and <paramref name="Kind"/> says on which side it sits — both
/// optional, both declared fact when present: without them a model sees "asset -> asset" and has
/// no way to know that a record's <c>asset_tag</c> is that relation rather than one more value.
/// </summary>
public sealed record DeclaredRelation(
    string Name,
    SubjectRef Target,
    string? ViaField = null,
    DeclaredRelationKind? Kind = null);
