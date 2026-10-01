using Eyu.Core.Declared;
using Eyu.Core.Proposals;
using Eyu.Core.Records;

namespace Eyu.Core.Ports;

/// <summary>
/// The core judgment: entities, relations, confidence, and entity resolution (merging records
/// that denote the same entity), from declared structure and/or sampled records. At least one of
/// a non-empty <c>declaredStructures</c> or a non-empty <c>records</c> must
/// be supplied — see design rationale §A on why judgment always requires some prior structure.
/// <c>declaredStructures</c> holds one declaration per subject: a caller that knows the
/// entity types and typed relations its records use declares each type as a subject — with fields
/// when it knows them, without when it knows only the name — and an empty list declares nothing.
/// Two declarations of the same subject are a caller error, since neither could be the authority.
/// Only the records given to a call can be cited as a claim's sources, so a call with no records
/// can return only an empty proposal, and a response that cites anything else is rejected rather
/// than passed through. An implementation that leaves individual elements of an answer out reports
/// each one in <see cref="OntologyProposal.Rejections"/> rather than dropping it silently.
///
/// This interface declaration does not commit to an internal implementation strategy; how the
/// judgment itself is organized (e.g. a single pass vs. multiple internal roles) is a separate,
/// benchmark-gated decision — see ROADMAP.md.
/// </summary>
public interface IOntologyProposer
{
    Task<OntologyProposal> ProposeAsync(
        IReadOnlyList<DeclaredStructure> declaredStructures,
        IReadOnlyList<RawRecord> records,
        CancellationToken cancellationToken = default)
        => ProposeAsync(declaredStructures, records, [], cancellationToken);

    /// <summary>
    /// The same judgment, told which entities earlier calls already identified. An entity the records
    /// show that is one of <paramref name="knownEntities"/> is proposed with that entity's key in
    /// <see cref="EntityProposal.KnownEntityKey"/> — the known entity wins, as a declaration does — so
    /// a caller that keeps proposals across calls (Eyu keeps none) can join them. A known entity's
    /// records are compared, never cited: a claim still cites only this call's records.
    /// </summary>
    Task<OntologyProposal> ProposeAsync(
        IReadOnlyList<DeclaredStructure> declaredStructures,
        IReadOnlyList<RawRecord> records,
        IReadOnlyList<KnownEntity> knownEntities,
        CancellationToken cancellationToken = default)
        => ProposeAsync(declaredStructures, records, knownEntities, [], cancellationToken);

    /// <summary>
    /// The same judgment, also told which entities earlier calls only mentioned. Identity then no longer
    /// depends on which source arrives first: an entity a record of this call denotes that may be one of
    /// <paramref name="mentionedEntities"/> is reported in <see cref="OntologyProposal.MergeCandidates"/>
    /// — a candidate the caller confirms or not, never an identity, since a mention claims nothing about
    /// what the entity is. Mentioned entities' records are compared, never cited, as known entities' are.
    /// This is the overload an implementer writes; the others forward to it with empty lists.
    /// </summary>
    Task<OntologyProposal> ProposeAsync(
        IReadOnlyList<DeclaredStructure> declaredStructures,
        IReadOnlyList<RawRecord> records,
        IReadOnlyList<KnownEntity> knownEntities,
        IReadOnlyList<MentionedEntity> mentionedEntities,
        CancellationToken cancellationToken = default);
}
