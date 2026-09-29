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
}
