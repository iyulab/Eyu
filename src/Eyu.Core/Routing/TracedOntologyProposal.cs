using Eyu.Core.Proposals;
using HoneAI;

namespace Eyu.Core.Routing;

/// <summary>
/// An <see cref="OntologyProposal"/> after a <see cref="RoutingPolicy"/> has been applied: the same
/// entities and relations, in the same order, each wrapped in the provenance stamp a consumer's
/// review gate reads. Produced by <see cref="ProposalRouting.Trace(OntologyProposal, RoutingPolicy)"/>.
/// </summary>
public sealed record TracedOntologyProposal(
    IReadOnlyList<ITracedPrediction<EntityProposal>> Entities,
    IReadOnlyList<ITracedPrediction<RelationProposal>> Relations);
