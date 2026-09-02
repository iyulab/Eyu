using Eyu.Core.Grounding;

namespace Eyu.Core.Proposals;

/// <summary>
/// A proposed entity — <paramref name="Claim"/>'s sources are the raw records this proposal
/// resolved as denoting the same real-world entity, so entity resolution is expressed as which
/// sources a single <see cref="EntityProposal"/> cites, not as a separate output. Construct only
/// via <see cref="Create"/>.
/// </summary>
public sealed record EntityProposal
{
    /// <summary>Identifies this entity within its containing <see cref="OntologyProposal"/> (see <see cref="RelationProposal"/>).</summary>
    public string EntityId { get; }

    public string EntityType { get; }
    public GroundedClaim Claim { get; }
    public VocabularyOrigin Origin { get; }

    /// <summary>
    /// Routing signal only — must be treated as uncalibrated unless the producing
    /// <c>IOntologyProposer</c> implementation states otherwise (design rationale §D).
    /// </summary>
    public double Confidence { get; }

    private EntityProposal(string entityId, string entityType, GroundedClaim claim, VocabularyOrigin origin, double confidence)
    {
        EntityId = entityId;
        EntityType = entityType;
        Claim = claim;
        Origin = origin;
        Confidence = confidence;
    }

    public static EntityProposal Create(string entityId, string entityType, GroundedClaim claim, VocabularyOrigin origin, double confidence)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            throw new ArgumentException("EntityId must be a non-empty identifier.", nameof(entityId));
        }

        if (string.IsNullOrWhiteSpace(entityType))
        {
            throw new ArgumentException("EntityType must be a non-empty identifier.", nameof(entityType));
        }

        if (confidence is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "Confidence must be within [0, 1].");
        }

        return new EntityProposal(entityId, entityType, claim, origin, confidence);
    }
}
