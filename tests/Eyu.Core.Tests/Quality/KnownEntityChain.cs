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

    /// <summary>
    /// What one call of a chain did with identity: how many known entities it was handed, how many of
    /// its entities it matched to one, which of the case's things it made known (denoted under a key
    /// the chain did not hold yet), and which it named only by mention — present in the call, matched
    /// to nothing known and denoted by no record, so the chain carries nothing of them forward. A thing
    /// that is mention-only at an early step and made known at a later one is identity that depends on
    /// arrival order.
    /// </summary>
    /// <param name="source">The source the call proposed.</param>
    /// <param name="proposal">The call's proposal.</param>
    /// <param name="records">The records the call was given.</param>
    /// <param name="keyOf">The caller's key for an entity, as for <see cref="FromProposal"/>.</param>
    /// <param name="knownIn">The known entities handed to this call.</param>
    /// <param name="crossSource">The case whose things the entities are joined to by name.</param>
    public static KnownChainStep Step(
        string source,
        OntologyProposal proposal,
        IReadOnlyList<RawRecord> records,
        Func<EntityProposal, string?> keyOf,
        IReadOnlyCollection<KnownEntity> knownIn,
        CrossSourceCase crossSource)
    {
        var held = knownIn.Select(k => k.Key).ToHashSet(StringComparer.Ordinal);
        var newlyKnown = FromProposal(proposal, records, keyOf).Where(k => !held.Contains(k.Key)).ToList();
        var newKeys = newlyKnown.Select(k => k.Key).ToHashSet(StringComparer.Ordinal);

        var madeKnown = new SortedSet<string>(StringComparer.Ordinal);
        var carried = new HashSet<string>(StringComparer.Ordinal);
        var named = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var entity in proposal.Entities)
        {
            var things = CrossSourceJoin.Match(crossSource, entity.Name).Select(t => t.Label).ToList();
            named.UnionWith(things);
            var key = entity.DenotedBy.Count > 0 ? keyOf(entity) : null;
            if (key is not null && newKeys.Contains(key))
            {
                madeKnown.UnionWith(things);
            }

            if (entity.KnownEntityKey is not null || key is not null)
            {
                carried.UnionWith(things);
            }
        }

        return new KnownChainStep(
            source,
            knownIn.Count,
            proposal.Entities.Count(e => e.KnownEntityKey is not null),
            newlyKnown.Count,
            [.. madeKnown],
            [.. named.Where(t => !carried.Contains(t))]);
    }
}

/// <summary>One call of a known-entity chain, as <see cref="KnownEntityChain.Step"/> measures it.</summary>
/// <param name="Source">The source the call proposed.</param>
/// <param name="KnownIn">Known entities handed to the call.</param>
/// <param name="MatchedToKnown">The call's entities matched to a known entity.</param>
/// <param name="NewlyKnown">Known entities the call adds to the chain.</param>
/// <param name="ThingsMadeKnown">The case's things the call made known.</param>
/// <param name="ThingsMentionedOnly">The case's things the call named only by mention — carried forward by nothing.</param>
internal sealed record KnownChainStep(
    string Source,
    int KnownIn,
    int MatchedToKnown,
    int NewlyKnown,
    IReadOnlyList<string> ThingsMadeKnown,
    IReadOnlyList<string> ThingsMentionedOnly);
