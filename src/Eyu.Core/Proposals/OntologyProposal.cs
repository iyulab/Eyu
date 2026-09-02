namespace Eyu.Core.Proposals;

/// <summary>The full output of one <c>IOntologyProposer.ProposeAsync</c> call.</summary>
public sealed record OntologyProposal(
    IReadOnlyList<EntityProposal> Entities,
    IReadOnlyList<RelationProposal> Relations);
