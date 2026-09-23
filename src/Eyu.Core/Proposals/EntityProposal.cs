using Eyu.Core.Grounding;

namespace Eyu.Core.Proposals;

/// <summary>
/// A proposed entity. <see cref="Claim"/>'s sources are every record the entity is grounded in —
/// where it appears. Of those, <see cref="DenotedBy"/> names the records that <em>are</em> this
/// entity (a row describing it as its own subject); the rest only refer to it, the way a work order
/// names the machine it ran on. Entity resolution is expressed as <see cref="DenotedBy"/>: two
/// records listed there are the proposal's claim that they denote the same entity, and a record
/// that merely mentions the entity makes no such claim. Where records do not each denote one entity
/// (<c>LinkageOptions.RecordsDenoteEntities</c> is <c>false</c>), no citation identifies the entity,
/// and <see cref="Name"/> is what carries it out of the call. Construct only via <see cref="Create"/>.
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

    /// <summary>
    /// The cited records that denote this entity — each is a record of the entity itself, so two of
    /// them are a claim that they are the same entity. Always a subset of <see cref="Claim"/>'s
    /// source records; empty when the entity is only mentioned.
    /// </summary>
    public IReadOnlyList<string> DenotedBy { get; }

    /// <summary>The cited records that only refer to this entity — <see cref="Claim"/>'s source records not in <see cref="DenotedBy"/>.</summary>
    public IReadOnlyList<string> MentionedIn => Claim.Sources.Select(s => s.RecordId).Distinct(StringComparer.Ordinal)
        .Where(id => !DenotedBy.Contains(id, StringComparer.Ordinal)).ToList();

    private EntityProposal(string entityId, string name, string entityType, GroundedClaim claim, VocabularyOrigin origin, double confidence, ProposalBasis basis, IReadOnlyList<string> denotedBy)
    {
        EntityId = entityId;
        Name = name;
        EntityType = entityType;
        Claim = claim;
        Origin = origin;
        Confidence = confidence;
        Basis = basis;
        DenotedBy = denotedBy;
    }

    /// <param name="entityId">See <see cref="EntityId"/>.</param>
    /// <param name="name">See <see cref="Name"/>.</param>
    /// <param name="entityType">See <see cref="EntityType"/>.</param>
    /// <param name="claim">See <see cref="Claim"/>.</param>
    /// <param name="origin">See <see cref="Origin"/>.</param>
    /// <param name="confidence">See <see cref="Confidence"/>.</param>
    /// <param name="basis">See <see cref="Basis"/>.</param>
    /// <param name="denotedBy">
    /// See <see cref="DenotedBy"/>. Omitted, every cited record denotes the entity — the reading a
    /// proposal built by hand from records that are each one mention of one entity intends. A record
    /// named here that the claim does not cite is refused: denoting an entity is the strongest way a
    /// record can be its evidence, so it cannot be absent from the evidence.
    /// </param>
    public static EntityProposal Create(string entityId, string name, string entityType, GroundedClaim claim, VocabularyOrigin origin, double confidence, ProposalBasis basis = ProposalBasis.Inferred, IReadOnlyList<string>? denotedBy = null)
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

        ArgumentNullException.ThrowIfNull(claim);
        var cited = claim.Sources.Select(s => s.RecordId).Distinct(StringComparer.Ordinal).ToList();
        var denoting = denotedBy is null ? cited : denotedBy.Distinct(StringComparer.Ordinal).ToList();
        var uncited = denoting.Where(id => !cited.Contains(id, StringComparer.Ordinal)).ToList();
        if (uncited.Count > 0)
        {
            throw new ArgumentException(
                $"A record that denotes the entity must also be one the claim cites: {string.Join(", ", uncited)}.",
                nameof(denotedBy));
        }

        return new EntityProposal(entityId, name, entityType, claim, origin, confidence, basis, denoting);
    }

    /// <summary>The same proposal with <see cref="Basis"/> set — used by the declared-structure merge, never by a model.</summary>
    internal EntityProposal WithBasis(ProposalBasis basis) => new(EntityId, Name, EntityType, Claim, Origin, Confidence, basis, DenotedBy);
}
