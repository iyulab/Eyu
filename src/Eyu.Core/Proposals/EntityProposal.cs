using Eyu.Core.Grounding;

namespace Eyu.Core.Proposals;

/// <summary>
/// A proposed entity — <paramref name="Claim"/>'s sources are the raw records this proposal
/// resolved as denoting the same real-world entity, so entity resolution is expressed as which
/// sources a single <see cref="EntityProposal"/> cites, not as a separate output. Where records do
/// not each denote one entity (<c>LinkageOptions.RecordsDenoteEntities</c> is <c>false</c>), the
/// cited sources no longer identify the entity, and <see cref="Name"/> is what carries it out of
/// the call. Construct only via <see cref="Create"/>.
/// </summary>
public sealed record EntityProposal
{
    /// <summary>Identifies this entity within its containing <see cref="OntologyProposal"/> (see <see cref="RelationProposal"/>).</summary>
    public string EntityId { get; }

    /// <summary>
    /// The entity as the records write it (its surface form, e.g. a company or person name) — the
    /// human-readable handle a caller persists, as opposed to <see cref="EntityId"/>, which means
    /// nothing outside this proposal. It is not normalized to any reference vocabulary.
    /// </summary>
    public string Name { get; }

    public string EntityType { get; }
    public GroundedClaim Claim { get; }
    public VocabularyOrigin Origin { get; }

    /// <summary>Whether <see cref="EntityType"/> is declared by the caller or inferred by the model — see <see cref="ProposalBasis"/>.</summary>
    public ProposalBasis Basis { get; }

    /// <summary>
    /// Routing signal only — must be treated as uncalibrated unless the producing
    /// <c>IOntologyProposer</c> implementation states otherwise (design rationale §D).
    /// </summary>
    public double Confidence { get; }

    private EntityProposal(string entityId, string name, string entityType, GroundedClaim claim, VocabularyOrigin origin, double confidence, ProposalBasis basis)
    {
        EntityId = entityId;
        Name = name;
        EntityType = entityType;
        Claim = claim;
        Origin = origin;
        Confidence = confidence;
        Basis = basis;
    }

    public static EntityProposal Create(string entityId, string name, string entityType, GroundedClaim claim, VocabularyOrigin origin, double confidence, ProposalBasis basis = ProposalBasis.Inferred)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            throw new ArgumentException("EntityId must be a non-empty identifier.", nameof(entityId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name must be a non-empty surface form.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(entityType))
        {
            throw new ArgumentException("EntityType must be a non-empty identifier.", nameof(entityType));
        }

        if (confidence is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "Confidence must be within [0, 1].");
        }

        return new EntityProposal(entityId, name, entityType, claim, origin, confidence, basis);
    }

    /// <summary>The same proposal with <see cref="Basis"/> set — used by the declared-structure merge, never by a model.</summary>
    internal EntityProposal WithBasis(ProposalBasis basis) => new(EntityId, Name, EntityType, Claim, Origin, Confidence, basis);
}
