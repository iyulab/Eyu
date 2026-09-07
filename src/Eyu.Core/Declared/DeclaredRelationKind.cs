namespace Eyu.Core.Declared;

/// <summary>
/// Which side of a declared relation carries the key that realises it — the fact a model needs in
/// order to recognise, in a record, the field that <em>is</em> the relation rather than treating
/// it as one more value.
/// </summary>
public enum DeclaredRelationKind
{
    /// <summary>This subject carries a field naming the target (a foreign-key style reference).</summary>
    Reference,

    /// <summary>The target carries a field naming this subject (the target is a child, one-to-many).</summary>
    Child,
}
