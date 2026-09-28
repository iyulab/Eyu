using Eyu.Core.Proposals;
using Eyu.Core.Records;

namespace Eyu.Core.Tests.Quality;

/// <summary>
/// What a caller that keeps proposals hands the next call: the entities one proposal identified, each
/// under the key the caller keeps it by, with the records that denoted it. Only entities some record
/// denotes become known — an entity nothing denotes has no record to compare a new one with — and
/// entities one key names become one known entity carrying all their records.
/// </summary>
internal static class KnownEntityChain
{
    /// <param name="proposal">The earlier proposal.</param>
    /// <param name="records">The records the earlier call was given — the denoting records are looked up here.</param>
    /// <param name="keyOf">The caller's key for an entity (an exporter passes the IRI it wrote); null leaves it out.</param>
    public static IReadOnlyList<KnownEntity> FromProposal(OntologyProposal proposal, IReadOnlyList<RawRecord> records, Func<EntityProposal, string?> keyOf)
    {
        var byId = records.ToDictionary(r => r.Id, StringComparer.Ordinal);
        return proposal.Entities
            .Where(e => e.DenotedBy.Count > 0)
            .Select(e => (Entity: e, Key: keyOf(e)))
            .Where(x => x.Key is not null)
            .GroupBy(x => x.Key!, StringComparer.Ordinal)
            .Select(g => new KnownEntity(
                g.Key,
                g.First().Entity.Name,
                g.First().Entity.EntityType,
                g.SelectMany(x => x.Entity.DenotedBy)
                    .Distinct(StringComparer.Ordinal)
                    .Where(byId.ContainsKey)
                    .Select(id => byId[id])
                    .ToList()))
            .Where(k => k.DenotingRecords.Count > 0)
            .OrderBy(k => k.Key, StringComparer.Ordinal)
            .ToList();
    }
}
