namespace Eyu.Core.Proposals;

/// <summary>
/// The full output of one <c>IOntologyProposer.ProposeAsync</c> call: the entities and relations
/// proposed, and every element of the model's answer that was left out together with the reason
/// (<see cref="ProposalRejection"/>). An empty <paramref name="Rejections"/> means the answer was
/// carried whole.
/// </summary>
public sealed record OntologyProposal(
    IReadOnlyList<EntityProposal> Entities,
    IReadOnlyList<RelationProposal> Relations,
    IReadOnlyList<ProposalRejection> Rejections)
{
    /// <summary>
    /// What the record-linkage pre-filter contributed and where the answer departed from it; null for
    /// a proposal no pre-filter ran for (one built directly rather than by a proposer).
    /// </summary>
    public LinkageReport? Linkage { get; init; }

    /// <summary>
    /// Proposed entities that may be the same thing as an entity an earlier call only mentioned —
    /// candidates for the caller to join or not, never identities (<see cref="MergeCandidate"/>). Empty
    /// when the call was given no mentioned entities.
    /// </summary>
    public IReadOnlyList<MergeCandidate> MergeCandidates { get; init; } = [];

    /// <summary>
    /// Records the answer claimed denote an entity but that only name it in a field the caller declared
    /// as a reference to another subject — each moved to the entity's mentions
    /// (<see cref="DemotedDenotation"/>). Empty when nothing was declared with a
    /// <see cref="Declared.DeclaredRelation.ViaField"/>, or the answer made no such claim.
    /// </summary>
    public IReadOnlyList<DemotedDenotation> DemotedDenotations { get; init; } = [];
}
