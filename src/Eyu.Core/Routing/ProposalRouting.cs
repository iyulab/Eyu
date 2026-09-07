using System.Text.Json;
using Eyu.Core.Grounding;
using Eyu.Core.Proposals;
using HoneAI;

namespace Eyu.Core.Routing;

/// <summary>
/// "Confidence routes, it doesn't decide" (README) as code a caller can call rather than a rule it
/// has to reimplement: <see cref="Route(EntityProposal, RoutingPolicy)"/> applies the caller's
/// thresholds, and <see cref="Trace(EntityProposal, RoutingPolicy)"/> additionally wraps the
/// proposal in HoneAI's <see cref="ITracedPrediction{T}"/> so the result can be handed to any
/// <c>IHitlGate</c>-style review flow. Eyu stamps the provenance and stops there — opening a gate,
/// awaiting a reviewer, applying an approved proposal are all the consumer's, because Eyu never
/// applies anything.
/// </summary>
public static class ProposalRouting
{
    /// <summary>The tier <paramref name="policy"/> assigns to this entity proposal's confidence, under its origin's thresholds.</summary>
    public static ProposalRoute Route(this EntityProposal proposal, RoutingPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(policy);
        return policy.For(proposal.Origin).Route(proposal.Confidence);
    }

    /// <summary>The tier <paramref name="policy"/> assigns to this relation proposal's confidence, under its origin's thresholds.</summary>
    public static ProposalRoute Route(this RelationProposal proposal, RoutingPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(policy);
        return policy.For(proposal.Origin).Route(proposal.Confidence);
    }

    /// <summary>This entity proposal with the provenance stamp its route implies — see <see cref="Stamp"/> for what each field carries.</summary>
    public static ITracedPrediction<EntityProposal> Trace(this EntityProposal proposal, RoutingPolicy policy)
    {
        var route = proposal.Route(policy);
        return new TracedPrediction<EntityProposal>(proposal, Stamp(route, proposal.Confidence, proposal.Claim, proposal.Origin, proposal.Basis));
    }

    /// <summary>This relation proposal with the provenance stamp its route implies — see <see cref="Stamp"/> for what each field carries.</summary>
    public static ITracedPrediction<RelationProposal> Trace(this RelationProposal proposal, RoutingPolicy policy)
    {
        var route = proposal.Route(policy);
        return new TracedPrediction<RelationProposal>(proposal, Stamp(route, proposal.Confidence, proposal.Claim, proposal.Origin, proposal.Basis));
    }

    /// <summary>Every proposal in <paramref name="proposal"/> traced under <paramref name="policy"/>, order preserved.</summary>
    public static TracedOntologyProposal Trace(this OntologyProposal proposal, RoutingPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(policy);
        return new TracedOntologyProposal(
            proposal.Entities.Select(e => e.Trace(policy)).ToList(),
            proposal.Relations.Select(r => r.Trace(policy)).ToList());
    }

    /// <summary>
    /// The stamp itself. <see cref="PredictionProvenance.SourceLayer"/> is always
    /// <see cref="ReasoningLayer.Frontier"/>: every proposal is a model's judgment, whatever
    /// pre-filtering shaped its confidence. <see cref="PredictionProvenance.RequiresReview"/> is true
    /// for both non-auto tiers — a draft-only proposal is not escalated the way a review is, but it
    /// is still nothing a machine may act on, which is the question the flag answers; the tier
    /// itself is in <see cref="ProvenanceAnnotations.Route"/>. <see cref="PredictionProvenance.Agreement"/>
    /// stays null because a single layer answered, and <see cref="PredictionProvenance.Rationale"/>
    /// is the claim text — the grounded sources travel as an annotation, not as prose.
    /// </summary>
    private static PredictionProvenance Stamp(ProposalRoute route, double confidence, GroundedClaim claim, VocabularyOrigin origin, ProposalBasis basis) =>
        new()
        {
            SourceLayer = ReasoningLayer.Frontier,
            Confidence = confidence,
            Rationale = claim.Claim,
            Agreement = null,
            RequiresReview = route != ProposalRoute.AutoApply,
            Annotations = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ProvenanceAnnotations.Route] = route.ToString(),
                [ProvenanceAnnotations.Origin] = origin.ToString(),
                [ProvenanceAnnotations.Basis] = basis.ToString(),
                [ProvenanceAnnotations.Sources] = JsonSerializer.Serialize(claim.Sources.Select(s => s.RecordId)),
            },
        };
}
