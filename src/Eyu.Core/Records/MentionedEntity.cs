namespace Eyu.Core.Records;

/// <summary>
/// An entity an earlier call only mentioned — named by its records, denoted by none of them — handed
/// back so a new call can say whether an entity it finds might be the same thing. Unlike a
/// <see cref="KnownEntity"/>, it is not a claim of identity: a work order that names its machine is
/// not a record of the machine, so nothing about the mention says which machine it is. A new call
/// that sees a record of the thing itself answers with a <see cref="Proposals.MergeCandidate"/>, never
/// with a <see cref="Proposals.EntityProposal.KnownEntityKey"/>, and the caller decides whether to join
/// the two.
/// </summary>
/// <param name="Key">The caller's identifier for the entity — opaque to Eyu, unique across the call's known and mentioned entities.</param>
/// <param name="Name">The entity as it was named.</param>
/// <param name="EntityType">The entity's type.</param>
/// <param name="MentioningRecords">
/// The records that mentioned the entity, with their fields — what a new record is compared against.
/// They are for comparison only: a claim in the new proposal can cite only the records of its own call.
/// </param>
public sealed record MentionedEntity(string Key, string Name, string EntityType, IReadOnlyList<RawRecord> MentioningRecords)
{
    /// <summary>
    /// Refuses a list of mentioned entities that cannot mean one thing: a blank key, name or type, a key
    /// given twice or also given to a known entity, or a mentioning record that is also one of the call's
    /// own records — a record cannot be both what was seen before and what is new. A mentioning record
    /// may also denote a known entity: a row that reports a repair denotes the repair and mentions the
    /// machine.
    /// </summary>
    internal static void Validate(IReadOnlyList<MentionedEntity> mentionedEntities, IReadOnlyList<KnownEntity> knownEntities, IReadOnlyList<RawRecord> records)
    {
        var knownKeys = knownEntities.Select(k => k.Key).ToHashSet(StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var mentioningRecordIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mentioned in mentionedEntities)
        {
            ArgumentNullException.ThrowIfNull(mentioned);
            if (string.IsNullOrWhiteSpace(mentioned.Key) || string.IsNullOrWhiteSpace(mentioned.Name) || string.IsNullOrWhiteSpace(mentioned.EntityType))
            {
                throw new ArgumentException("A mentioned entity needs a non-blank key, name and type.", nameof(mentionedEntities));
            }

            if (!keys.Add(mentioned.Key))
            {
                throw new ArgumentException($"The mentioned entity key '{mentioned.Key}' is given twice — a key names one entity.", nameof(mentionedEntities));
            }

            if (knownKeys.Contains(mentioned.Key))
            {
                throw new ArgumentException($"The key '{mentioned.Key}' names both a known and a mentioned entity — a key names one entity, and it is either known or only mentioned.", nameof(mentionedEntities));
            }

            foreach (var record in mentioned.MentioningRecords ?? [])
            {
                mentioningRecordIds.Add(record.Id);
            }
        }

        var overlap = records.Select(r => r.Id).Where(mentioningRecordIds.Contains).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (overlap.Count > 0)
        {
            throw new ArgumentException(
                $"The record(s) {string.Join(", ", overlap)} are both among this call's records and a mentioned entity's — a record is either new or already seen.",
                nameof(mentionedEntities));
        }
    }
}
