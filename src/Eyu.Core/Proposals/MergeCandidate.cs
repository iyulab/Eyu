using Eyu.Core.Grounding;

namespace Eyu.Core.Proposals;

/// <summary>
/// A proposed entity that may be the same thing as an entity an earlier call only mentioned
/// (<see cref="Records.MentionedEntity"/>). It is a candidate, not an identity: only a record that
/// denotes an entity claims what the entity is, and a mention never did, so the caller decides whether
/// to join the two. The candidate always comes from the side that has such a record —
/// <see cref="EntityId"/> names an entity of the same proposal with a non-empty
/// <see cref="EntityProposal.DenotedBy"/> — and its <see cref="Claim"/> cites only this call's records.
/// Construct only via <see cref="Create"/>.
/// </summary>
public sealed record MergeCandidate
{
    /// <summary>The proposed entity, by its <see cref="EntityProposal.EntityId"/>.</summary>
    public string EntityId { get; }

    /// <summary>The mentioned entity it may be, by the <see cref="Records.MentionedEntity.Key"/> the caller gave.</summary>
    public string MentionedEntityKey { get; }

    /// <summary>Why the two may be one thing, citing this call's records.</summary>
    public GroundedClaim Claim { get; }

    /// <summary>
    /// The model's own confidence, unadjusted: record linkage has no prior to offer for a mention, so
    /// there is nothing to combine it with. A routing signal, as <see cref="EntityProposal.Confidence"/> is.
    /// </summary>
    public double Confidence { get; }

    private MergeCandidate(string entityId, string mentionedEntityKey, GroundedClaim claim, double confidence)
    {
        EntityId = entityId;
        MentionedEntityKey = mentionedEntityKey;
        Claim = claim;
        Confidence = confidence;
    }

    public static MergeCandidate Create(string entityId, string mentionedEntityKey, GroundedClaim claim, double confidence)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            throw new ArgumentException("EntityId must be a non-empty identifier.", nameof(entityId));
        }

        if (string.IsNullOrWhiteSpace(mentionedEntityKey))
        {
            throw new ArgumentException("MentionedEntityKey must be a non-empty identifier.", nameof(mentionedEntityKey));
        }

        ArgumentNullException.ThrowIfNull(claim);
        if (confidence is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "Confidence must be within [0, 1].");
        }

        return new MergeCandidate(entityId, mentionedEntityKey, claim, confidence);
    }
}
