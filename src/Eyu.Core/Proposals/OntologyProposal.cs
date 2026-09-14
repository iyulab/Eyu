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
    IReadOnlyList<ProposalRejection> Rejections);
