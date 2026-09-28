namespace Eyu.Core.Records;

/// <summary>
/// An entity an earlier call already identified, handed back so a new call can tell whether a record
/// it is given denotes the same thing. Eyu keeps nothing between calls: whoever stores proposals decides
/// which entities are known and passes them in, and a proposal that matches one says so through
/// <see cref="Proposals.EntityProposal.KnownEntityKey"/> instead of standing up a new entity.
/// </summary>
/// <param name="Key">
/// The caller's identifier for the entity — opaque to Eyu. A caller that exports RDF will usually pass
/// the IRI an earlier export gave it, so a match keeps that IRI.
/// </param>
/// <param name="Name">The entity as it was named.</param>
/// <param name="EntityType">The entity's type.</param>
/// <param name="DenotingRecords">
/// The records that denoted the entity, with their fields — what a new record is compared against.
/// They are for comparison only: a claim in the new proposal can cite only the records of its own call.
/// </param>
public sealed record KnownEntity(string Key, string Name, string EntityType, IReadOnlyList<RawRecord> DenotingRecords)
{
    /// <summary>
    /// Refuses a list of known entities that cannot mean one thing: a blank key, name or type, a key
    /// given twice, or a known record that is also one of the call's own records — a record cannot be
    /// both what is already known and what is new. One record may denote several known entities: a row
    /// that reports an event denotes the event and the machine it happened to alike.
    /// </summary>
    internal static void Validate(IReadOnlyList<KnownEntity> knownEntities, IReadOnlyList<RawRecord> records)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var knownRecordIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var known in knownEntities)
        {
            ArgumentNullException.ThrowIfNull(known);
            if (string.IsNullOrWhiteSpace(known.Key) || string.IsNullOrWhiteSpace(known.Name) || string.IsNullOrWhiteSpace(known.EntityType))
            {
                throw new ArgumentException("A known entity needs a non-blank key, name and type.", nameof(knownEntities));
            }

            if (!keys.Add(known.Key))
            {
                throw new ArgumentException($"The known entity key '{known.Key}' is given twice — a key names one entity.", nameof(knownEntities));
            }

            foreach (var record in known.DenotingRecords ?? [])
            {
                knownRecordIds.Add(record.Id);
            }
        }

        var overlap = records.Select(r => r.Id).Where(knownRecordIds.Contains).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (overlap.Count > 0)
        {
            throw new ArgumentException(
                $"The record(s) {string.Join(", ", overlap)} are both among this call's records and a known entity's — a record is either new or already known.",
                nameof(knownEntities));
        }
    }
}
