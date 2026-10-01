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
            [.. named.Where(t => !carried.Contains(t))])
        {
            MergeCandidates = proposal.MergeCandidates.Count,
            DemotedDenotations = proposal.DemotedDenotations.Count,
        };
    }

    /// <summary>
    /// The entities a proposal only mentioned, for a caller that also hands those forward: entities no
    /// record denotes and that matched nothing known, each under the caller's key, with the records that
    /// cited them. Entities one key names become one mentioned entity carrying all their records.
    /// </summary>
    /// <param name="proposal">The earlier proposal.</param>
    /// <param name="records">The records the earlier call was given — the citing records are looked up here.</param>
    /// <param name="keyOf">The caller's key for an entity; null leaves it out.</param>
    public static IReadOnlyList<MentionedEntity> MentionedFromProposal(OntologyProposal proposal, IReadOnlyList<RawRecord> records, Func<EntityProposal, string?> keyOf)
    {
        var byId = records.ToDictionary(r => r.Id, StringComparer.Ordinal);
        return proposal.Entities
            .Where(e => e.DenotedBy.Count == 0 && e.KnownEntityKey is null)
            .Select(e => (Entity: e, Key: keyOf(e)))
            .Where(x => x.Key is not null)
            .GroupBy(x => x.Key!, StringComparer.Ordinal)
            .Select(g => new MentionedEntity(
                g.Key,
                g.First().Entity.Name,
                g.First().Entity.EntityType,
                g.SelectMany(x => x.Entity.Claim.Sources.Select(s => s.RecordId))
                    .Distinct(StringComparer.Ordinal)
                    .Where(byId.ContainsKey)
                    .Select(id => byId[id])
                    .ToList()))
            .Where(m => m.MentioningRecords.Count > 0)
            .OrderBy(m => m.Key, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Adds <paramref name="additions"/> to <paramref name="held"/>: a key already held gains the new
    /// records it was mentioned by, a new key is appended, and a key that has become known is dropped —
    /// a thing a record now denotes is no longer only mentioned.
    /// </summary>
    public static void AccumulateMentioned(List<MentionedEntity> held, IEnumerable<MentionedEntity> additions, IReadOnlyCollection<KnownEntity> known)
    {
        foreach (var added in additions)
        {
            var index = held.FindIndex(m => m.Key == added.Key);
            if (index < 0)
            {
                held.Add(added);
                continue;
            }

            var ids = held[index].MentioningRecords.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
            held[index] = held[index] with { MentioningRecords = [.. held[index].MentioningRecords, .. added.MentioningRecords.Where(r => !ids.Contains(r.Id))] };
        }

        var knownKeys = known.Select(k => k.Key).ToHashSet(StringComparer.Ordinal);
        held.RemoveAll(m => knownKeys.Contains(m.Key));
    }

    /// <summary>
    /// Judges a merge candidate against the case: correct when the candidate entity and the mentioned
    /// entity are joined to a common thing, wrong when each is joined to some thing but never the same
    /// one (an OAEI <em>false</em> mapping), unjudged when either names no thing of the case.
    /// </summary>
    public static CandidateVerdict Judge(string entityName, string mentionedName, CrossSourceCase crossSource)
    {
        var own = CrossSourceJoin.Match(crossSource, entityName).Select(t => t.Label).ToHashSet(StringComparer.Ordinal);
        var mentioned = CrossSourceJoin.Match(crossSource, mentionedName).Select(t => t.Label).ToHashSet(StringComparer.Ordinal);
        return own.Count == 0 || mentioned.Count == 0 ? CandidateVerdict.Unjudged
            : own.Overlaps(mentioned) ? CandidateVerdict.Correct
            : CandidateVerdict.Wrong;
    }
}

/// <summary>How a merge candidate stands against the case's things (<see cref="KnownEntityChain.Judge"/>).</summary>
public enum CandidateVerdict
{
    Correct,
    Wrong,
    Unjudged,
}

/// <summary>
/// The individuals a caller holds once it confirms merge candidates: each confirmed candidate joins the
/// key the mentioned entity was kept under and the IRI the candidate entity was written under, and
/// <see cref="Find"/> returns one representative per joined group — the union-find a store applying
/// the confirmations would amount to.
/// </summary>
internal sealed class ConfirmedIdentity
{
    private readonly Dictionary<string, string> parent = new(StringComparer.Ordinal);

    public void Join(string a, string b)
    {
        var (ra, rb) = (Find(a), Find(b));
        if (ra != rb)
        {
            parent[string.CompareOrdinal(ra, rb) < 0 ? rb : ra] = string.CompareOrdinal(ra, rb) < 0 ? ra : rb;
        }
    }

    public string Find(string key)
    {
        var current = key;
        while (parent.TryGetValue(current, out var next))
        {
            current = next;
        }

        return current;
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
    IReadOnlyList<string> ThingsMentionedOnly)
{
    /// <summary>Merge candidates the call reported — 0 for a chain that hands forward no mentioned entities.</summary>
    public int MergeCandidates { get; init; }

    /// <summary>
    /// Denotations the call withdrew because the record named the entity in a declared reference field
    /// (<see cref="OntologyProposal.DemotedDenotations"/>) — 0 for a source that declares none.
    /// </summary>
    public int DemotedDenotations { get; init; }
}
