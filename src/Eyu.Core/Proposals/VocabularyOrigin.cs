namespace Eyu.Core.Proposals;

/// <summary>
/// Which vocabulary a proposal drew on — see design rationale §C. Innate and acquired proposals
/// do not share a calibration curve: a caller doing confidence-based routing must tune thresholds
/// per origin, not against one pooled distribution.
/// </summary>
public enum VocabularyOrigin
{
    /// <summary>Drawn from the small, domain-independent vocabulary (Person, Organization, Event, ...).</summary>
    Innate,

    /// <summary>Drawn from domain-specific categories learned from this caller's accumulated records.</summary>
    Acquired,
}
