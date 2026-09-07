using Eyu.Core.Grounding;

namespace Eyu.Core.Proposals;

/// <summary>
/// A proposed relation between two entities proposed in the same <see cref="OntologyProposal"/>,
/// referenced by <see cref="EntityProposal.EntityId"/>. Construct only via <see cref="Create"/>.
/// </summary>
public sealed record RelationProposal
{
    public string RelationName { get; }
    public string FromEntityId { get; }
    public string ToEntityId { get; }
    public GroundedClaim Claim { get; }
    public VocabularyOrigin Origin { get; }

    /// <summary>Whether <see cref="RelationName"/> is declared by the caller or inferred by the model — see <see cref="ProposalBasis"/>.</summary>
    public ProposalBasis Basis { get; }

    /// <summary>Routing signal only — see <see cref="EntityProposal.Confidence"/> for the same caveat.</summary>
    public double Confidence { get; }

    private RelationProposal(string relationName, string fromEntityId, string toEntityId, GroundedClaim claim, VocabularyOrigin origin, double confidence, ProposalBasis basis)
    {
        RelationName = relationName;
        FromEntityId = fromEntityId;
        ToEntityId = toEntityId;
        Claim = claim;
        Origin = origin;
        Confidence = confidence;
        Basis = basis;
    }

    public static RelationProposal Create(string relationName, string fromEntityId, string toEntityId, GroundedClaim claim, VocabularyOrigin origin, double confidence, ProposalBasis basis = ProposalBasis.Inferred)
    {
        if (string.IsNullOrWhiteSpace(relationName))
        {
            throw new ArgumentException("RelationName must be a non-empty identifier.", nameof(relationName));
        }

        if (string.IsNullOrWhiteSpace(fromEntityId))
        {
            throw new ArgumentException("FromEntityId must be a non-empty identifier.", nameof(fromEntityId));
        }

        if (string.IsNullOrWhiteSpace(toEntityId))
        {
            throw new ArgumentException("ToEntityId must be a non-empty identifier.", nameof(toEntityId));
        }

        if (confidence is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "Confidence must be within [0, 1].");
        }

        return new RelationProposal(relationName, fromEntityId, toEntityId, claim, origin, confidence, basis);
    }

    /// <summary>The same proposal with <see cref="Basis"/> set — used by the declared-structure merge, never by a model.</summary>
    internal RelationProposal WithBasis(ProposalBasis basis) => new(RelationName, FromEntityId, ToEntityId, Claim, Origin, Confidence, basis);
}
