namespace Eyu.Core.Proposals;

/// <summary>
/// Which vocabulary a proposal drew on — see design rationale §C. Innate and acquired proposals
/// do not share a calibration curve: a caller doing confidence-based routing must tune thresholds
/// per origin, not against one pooled distribution. What counts as innate is the closed
/// <see cref="InnateVocabulary"/>; the bundled proposer stamps origin from it rather than asking the
/// model.
/// </summary>
public enum VocabularyOrigin
{
    /// <summary>A type or relation name in <see cref="InnateVocabulary"/> — the small, domain-independent vocabulary.</summary>
    Innate,

    /// <summary>Any other type or relation name — a domain-specific category.</summary>
    Acquired,
}
