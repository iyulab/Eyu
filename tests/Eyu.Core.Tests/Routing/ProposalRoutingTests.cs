using System.Text.Json;
using Eyu.Core.Grounding;
using Eyu.Core.Proposals;
using Eyu.Core.Routing;
using HoneAI;
using Xunit;

namespace Eyu.Core.Tests.Routing;

/// <summary>
/// "A caller-defined threshold routes it to auto-apply, human review, or draft-only" (README) —
/// checked at the boundaries, per origin, and through the HoneAI provenance stamp a consumer's
/// review gate would read.
/// </summary>
public class ProposalRoutingTests
{
    private static readonly RoutingPolicy Policy = RoutingPolicy.Create(
        innate: new RoutingThresholds(AutoApplyAt: 0.8, ReviewAt: 0.5),
        acquired: new RoutingThresholds(AutoApplyAt: 0.95, ReviewAt: 0.7));

    private static GroundedClaim Claim() =>
        GroundedClaim.Create("rec-1 and rec-2 denote the same person", sources: [new SourceRef("rec-1"), new SourceRef("rec-2")]);

    private static EntityProposal Entity(double confidence, VocabularyOrigin origin = VocabularyOrigin.Innate, ProposalBasis basis = ProposalBasis.Inferred) =>
        EntityProposal.Create("e1", "Person", Claim(), origin, confidence, basis);

    private static RelationProposal Relation(double confidence, VocabularyOrigin origin = VocabularyOrigin.Innate) =>
        RelationProposal.Create("employs", "e1", "e2", Claim(), origin, confidence);

    [Theory]
    [InlineData(1.0, ProposalRoute.AutoApply)]
    [InlineData(0.8, ProposalRoute.AutoApply)]
    [InlineData(0.79, ProposalRoute.Review)]
    [InlineData(0.5, ProposalRoute.Review)]
    [InlineData(0.49, ProposalRoute.DraftOnly)]
    [InlineData(0.0, ProposalRoute.DraftOnly)]
    public void An_entity_routes_by_its_confidence_with_inclusive_lower_bounds(double confidence, ProposalRoute expected)
    {
        Assert.Equal(expected, Entity(confidence).Route(Policy));
    }

    [Theory]
    [InlineData(0.8, ProposalRoute.AutoApply)]
    [InlineData(0.5, ProposalRoute.Review)]
    [InlineData(0.49, ProposalRoute.DraftOnly)]
    public void A_relation_routes_the_same_way_as_an_entity(double confidence, ProposalRoute expected)
    {
        Assert.Equal(expected, Relation(confidence).Route(Policy));
    }

    [Fact]
    public void The_same_confidence_routes_differently_per_origin()
    {
        Assert.Equal(ProposalRoute.AutoApply, Entity(0.85, VocabularyOrigin.Innate).Route(Policy));
        Assert.Equal(ProposalRoute.Review, Entity(0.85, VocabularyOrigin.Acquired).Route(Policy));
        Assert.Equal(ProposalRoute.Review, Entity(0.6, VocabularyOrigin.Innate).Route(Policy));
        Assert.Equal(ProposalRoute.DraftOnly, Entity(0.6, VocabularyOrigin.Acquired).Route(Policy));
    }

    [Fact]
    public void Tracing_stamps_the_frontier_layer_and_passes_the_confidence_through_unchanged()
    {
        var proposal = Entity(0.85);

        var traced = proposal.Trace(Policy);

        Assert.Same(proposal, traced.Value);
        Assert.Equal(ReasoningLayer.Frontier, traced.Provenance.SourceLayer);
        Assert.Equal(0.85, traced.Provenance.Confidence);
        Assert.Null(traced.Provenance.Agreement);
        Assert.Null(traced.Provenance.Role);
    }

    [Theory]
    [InlineData(0.9, false)]
    [InlineData(0.6, true)]
    [InlineData(0.1, true)]
    public void RequiresReview_is_set_for_every_tier_a_machine_may_not_act_on(double confidence, bool requiresReview)
    {
        Assert.Equal(requiresReview, Entity(confidence).Trace(Policy).Provenance.RequiresReview);
    }

    [Fact]
    public void The_annotations_carry_what_the_stamp_has_no_field_for()
    {
        var traced = Entity(0.6, VocabularyOrigin.Acquired, ProposalBasis.Declared).Trace(Policy);
        var annotations = traced.Provenance.Annotations;

        Assert.NotNull(annotations);
        Assert.Equal("DraftOnly", annotations[ProvenanceAnnotations.Route]);
        Assert.Equal("Acquired", annotations[ProvenanceAnnotations.Origin]);
        Assert.Equal("Declared", annotations[ProvenanceAnnotations.Basis]);
        Assert.Equal("""["rec-1","rec-2"]""", annotations[ProvenanceAnnotations.Sources]);
    }

    [Fact]
    public void Cited_ids_survive_the_annotation_even_when_they_carry_delimiters()
    {
        string[] ids = ["rec,1", "rec\"2", "rec 3"];
        var claim = GroundedClaim.Create("these denote the same person", sources: [.. ids.Select(id => new SourceRef(id))]);
        var traced = EntityProposal.Create("e1", "Person", claim, VocabularyOrigin.Innate, 0.9, ProposalBasis.Inferred).Trace(Policy);

        var roundTripped = JsonSerializer.Deserialize<string[]>(traced.Provenance.Annotations![ProvenanceAnnotations.Sources]);

        Assert.Equal(ids, roundTripped);
    }

    [Fact]
    public void The_rationale_is_the_claim_text()
    {
        var proposal = Relation(0.9);
        Assert.Equal(proposal.Claim.Claim, proposal.Trace(Policy).Provenance.Rationale);
    }

    [Fact]
    public void Tracing_a_whole_proposal_keeps_every_item_in_order_and_wraps_the_originals()
    {
        var entities = new[] { Entity(0.9), Entity(0.6), Entity(0.2) };
        var relations = new[] { Relation(0.9), Relation(0.3) };
        var proposal = new OntologyProposal(entities, relations);

        var traced = proposal.Trace(Policy);

        Assert.Equal(entities, traced.Entities.Select(t => t.Value));
        Assert.Equal(relations, traced.Relations.Select(t => t.Value));
        Assert.Equal(
            [ProposalRoute.AutoApply, ProposalRoute.Review, ProposalRoute.DraftOnly],
            traced.Entities.Select(t => Enum.Parse<ProposalRoute>(t.Provenance.Annotations![ProvenanceAnnotations.Route])));
        Assert.Equal([false, true], traced.Relations.Select(t => t.Provenance.RequiresReview));
    }

    [Fact]
    public void A_null_policy_is_refused_before_anything_is_routed()
    {
        Assert.Throws<ArgumentNullException>(() => Entity(0.9).Route(null!));
        Assert.Throws<ArgumentNullException>(() => new OntologyProposal([], []).Trace(null!));
    }
}
