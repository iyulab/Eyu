namespace Eyu.Core.Records;

/// <summary>
/// What earlier calls already found, handed to a new call — Eyu keeps nothing between calls, so whoever
/// stores proposals decides what of them to pass back. Each part is optional and each changes the
/// request only when it is non-empty, so a call that passes <see cref="None"/> sends the request a call
/// without it always sent.
/// </summary>
/// <param name="KnownEntities">
/// Entities earlier calls identified. One the records show is proposed with its key in
/// <see cref="Proposals.EntityProposal.KnownEntityKey"/> and keeps its type.
/// </param>
/// <param name="MentionedEntities">
/// Entities earlier calls only saw named. One a record of this call may denote is reported as a
/// <see cref="Proposals.MergeCandidate"/>, never as an identity.
/// </param>
/// <param name="KnownTypes">
/// Type names earlier calls use. An entity of a kind one of them names is proposed under that name.
/// </param>
public sealed record PriorKnowledge(
    IReadOnlyList<KnownEntity> KnownEntities,
    IReadOnlyList<MentionedEntity> MentionedEntities,
    IReadOnlyList<KnownType> KnownTypes)
{
    /// <summary>Nothing from earlier calls.</summary>
    public static PriorKnowledge None { get; } = new([], [], []);

    /// <summary>Only known entities.</summary>
    public static PriorKnowledge Of(IReadOnlyList<KnownEntity> knownEntities) => new(knownEntities, [], []);

    /// <summary>Only known types.</summary>
    public static PriorKnowledge Of(IReadOnlyList<KnownType> knownTypes) => new([], [], knownTypes);

    /// <summary>Refuses prior knowledge that cannot mean one thing — each part by its own rules.</summary>
    internal void Validate(IReadOnlyList<RawRecord> records)
    {
        ArgumentNullException.ThrowIfNull(KnownEntities);
        ArgumentNullException.ThrowIfNull(MentionedEntities);
        ArgumentNullException.ThrowIfNull(KnownTypes);
        KnownEntity.Validate(KnownEntities, records);
        MentionedEntity.Validate(MentionedEntities, KnownEntities, records);
        KnownType.Validate(KnownTypes, records);
    }
}
