namespace Eyu.Core.Proposals;

/// <summary>Which part of a model response a <see cref="ProposalRejection"/> refers to.</summary>
public enum ProposalElement
{
    Entity,
    Relation,
}

/// <summary>
/// Why one element of a model response was left out of an <see cref="OntologyProposal"/>. Each
/// reason is a deterministic check, not a judgment — the model's answer is incomplete or
/// incoherent at that element, and the rest of the answer is independent of it.
/// </summary>
public enum RejectionReason
{
    /// <summary>A required field is absent or blank, or an entity or relation cites no source.</summary>
    MissingField,

    /// <summary>The confidence is outside [0, 1] — reported rather than clamped.</summary>
    ConfidenceOutOfRange,

    /// <summary>More than one entity was proposed under this id, so no relation to it can be resolved; every entity under it is left out.</summary>
    DuplicateEntityId,

    /// <summary>A relation end names an id the response never proposed as an entity.</summary>
    DanglingRelationEnd,

    /// <summary>A relation end names an entity the response proposed but that was itself rejected.</summary>
    EndpointRejected,

    /// <summary>A relation uses a declared relation's name for ends the declaration does not describe — "Declared always wins".</summary>
    ContradictsDeclaration,
}

/// <summary>
/// One element of a model response that an <see cref="OntologyProposal"/> does not carry, and why.
/// Rejections are part of the result rather than a log line: a dropped element that nobody can
/// see would hide exactly the model-quality signal a caller measures, so a caller that wants
/// all-or-nothing behavior checks <c>Rejections.Count</c> and a caller that measures counts
/// <see cref="Reason"/>.
/// </summary>
/// <param name="Element">Whether an entity or a relation was left out.</param>
/// <param name="Id">The entity's response-local id, or the relation's name; <c>null</c> when the model omitted it.</param>
/// <param name="Reason">The check the element failed.</param>
/// <param name="Detail">A human-readable account naming the offending values.</param>
public sealed record ProposalRejection(ProposalElement Element, string? Id, RejectionReason Reason, string Detail);
