namespace Eyu.Core.Proposals;

/// <summary>
/// Whether a proposal's <em>type</em> — an entity's type, a relation's name — is something the
/// caller declared or something the model inferred. Orthogonal to <see cref="VocabularyOrigin"/>:
/// that says which vocabulary a type was drawn from, this says whether the caller already stated
/// it. "Declared always wins" (README) is enforced on this axis, deterministically, after the model
/// answers: a proposal that matches declared structure is stamped <see cref="Declared"/> regardless
/// of what the model said about itself, and a relation proposed under a declared name whose ends
/// contradict the declaration is not returned at all. The <em>instance</em> — that these records
/// stand in that relation — remains the model's judgment, which is why confidence is untouched.
/// </summary>
public enum ProposalBasis
{
    /// <summary>The model inferred this type; nothing the caller declared names it.</summary>
    Inferred,

    /// <summary>The caller's declared structure names this type — the declaration is the answer.</summary>
    Declared,
}
