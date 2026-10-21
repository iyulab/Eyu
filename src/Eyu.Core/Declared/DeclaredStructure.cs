using Eyu.Core.Primitives;

namespace Eyu.Core.Declared;

/// <summary>
/// What a caller has already declared for <paramref name="Subject"/> — field hints, relations, and
/// an opaque <paramref name="Version"/> a consumer may use to detect redeclaration. Declared
/// structure always wins over inference (README: "Declared always wins").
/// <paramref name="BusinessName"/> is the name a person reads the subject by («설비 점검일지» for
/// <c>inspection_log</c>), as <see cref="DeclaredField.BusinessName"/> is for a field: the model is shown
/// it beside the declared type, and the declared type stays <paramref name="Subject"/> — an entity
/// proposed under the business name is written under the subject's name.
/// </summary>
public sealed record DeclaredStructure(
    SubjectRef Subject,
    IReadOnlyList<DeclaredField> Fields,
    IReadOnlyList<DeclaredRelation> Relations,
    string? Version = null,
    string? BusinessName = null)
{
    /// <summary>
    /// The types <see cref="Subject"/> is declared a kind of (<c>is-a</c>) — declared fact, as the rest of
    /// the declaration is. A declaration is a floor: other super types may still be proposed. A call
    /// whose declarations name any super type is asked for type relations
    /// (<see cref="Proposals.OntologyProposal.TypeRelations"/>), and one proposed for a declared pair is
    /// stamped declared.
    /// </summary>
    public IReadOnlyList<string> SuperTypes { get; init; } = [];
}
