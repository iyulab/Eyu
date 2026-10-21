using Eyu.Core.Grounding;

namespace Eyu.Core.Proposals;

/// <summary>
/// A proposed relation between two types: <see cref="SubType"/> is a kind of <see cref="SuperType"/>
/// (<c>is-a</c>) — a claim about the types, not about any entity of them. Either end may be a type no
/// entity of this proposal carries (a record may say what a kind of thing is without being a record of
/// one). Like every claim it cites the records it rests on; a type relation the records do not support
/// is not proposed. Construct only via <see cref="Create"/>.
/// </summary>
public sealed record TypeRelationProposal
{
    /// <summary>The narrower type.</summary>
    public string SubType { get; }

    /// <summary>The broader type.</summary>
    public string SuperType { get; }

    public GroundedClaim Claim { get; }

    /// <summary>
    /// <see cref="ProposalBasis.Declared"/> when the caller declared this pair
    /// (<see cref="Declared.DeclaredStructure.SuperTypes"/>), whatever the model said; otherwise
    /// <see cref="ProposalBasis.Inferred"/>.
    /// </summary>
    public ProposalBasis Basis { get; }

    /// <summary>Routing signal only — see <see cref="EntityProposal.Confidence"/> for the same caveat.</summary>
    public double Confidence { get; }

    private TypeRelationProposal(string subType, string superType, GroundedClaim claim, double confidence, ProposalBasis basis)
    {
        SubType = subType;
        SuperType = superType;
        Claim = claim;
        Confidence = confidence;
        Basis = basis;
    }

    /// <summary>
    /// Refuses a blank end, an end that is the other end spelled another way
    /// (<see cref="Records.TypeNames.SameType"/> — a type is not a kind of itself), and a confidence outside [0, 1].
    /// </summary>
    public static TypeRelationProposal Create(string subType, string superType, GroundedClaim claim, double confidence, ProposalBasis basis = ProposalBasis.Inferred)
    {
        if (string.IsNullOrWhiteSpace(subType))
        {
            throw new ArgumentException("SubType must be a non-empty type name.", nameof(subType));
        }

        if (string.IsNullOrWhiteSpace(superType))
        {
            throw new ArgumentException("SuperType must be a non-empty type name.", nameof(superType));
        }

        if (Records.TypeNames.SameType(subType, superType))
        {
            throw new ArgumentException($"A type is not a kind of itself: '{subType}' and '{superType}' name one type.", nameof(superType));
        }

        if (confidence is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "Confidence must be within [0, 1].");
        }

        ArgumentNullException.ThrowIfNull(claim);
        return new TypeRelationProposal(subType.Trim(), superType.Trim(), claim, confidence, basis);
    }

    /// <summary>The same proposal with <see cref="Basis"/> set — used by the declared-structure merge, never by a model.</summary>
    internal TypeRelationProposal WithBasis(ProposalBasis basis) => new(SubType, SuperType, Claim, Confidence, basis);
}
