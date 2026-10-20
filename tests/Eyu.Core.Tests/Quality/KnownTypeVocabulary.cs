using Eyu.Core.Proposals;
using Eyu.Core.Records;

namespace Eyu.Core.Tests.Quality;

/// <summary>
/// The type names a caller that keeps proposals holds, and what it hands a call of them. A type is a
/// name in use once some proposal wrote an entity under it; its specimens are records that denoted such
/// an entity — a record that only mentions one (a purchase order naming a part) is not a record of the
/// kind, so a type no record denotes is handed by name alone.
/// </summary>
internal static class KnownTypeVocabulary
{
    /// <summary>
    /// The vocabulary the proposals hold, in the order they wrote it: one entry per name (spellings that
    /// differ only in case and separators are one name, the first spelling kept), with up to
    /// <paramref name="specimensPerType"/> denoting records.
    /// </summary>
    /// <param name="proposals">Each proposal with the records its call was given — the specimens are looked up there.</param>
    /// <param name="specimensPerType">How many records to keep per type, so the prompt stays bounded.</param>
    public static IReadOnlyList<KnownType> FromProposals(IEnumerable<(OntologyProposal Proposal, IReadOnlyList<RawRecord> Records)> proposals, int specimensPerType = 2)
    {
        var names = new List<string>();
        var specimens = new Dictionary<string, List<RawRecord>>(StringComparer.Ordinal);
        foreach (var (proposal, records) in proposals)
        {
            var byId = records.ToDictionary(r => r.Id, StringComparer.Ordinal);
            foreach (var entity in proposal.Entities)
            {
                var name = names.FirstOrDefault(n => TypeNames.SameType(n, entity.EntityType));
                if (name is null)
                {
                    name = entity.EntityType.Trim();
                    names.Add(name);
                    specimens[name] = [];
                }

                foreach (var id in entity.DenotedBy.Where(byId.ContainsKey))
                {
                    if (specimens[name].Count < specimensPerType && specimens[name].All(r => r.Id != id))
                    {
                        specimens[name].Add(byId[id]);
                    }
                }
            }
        }

        return names.Select(n => new KnownType(n, specimens[n])).ToList();
    }

    /// <summary>
    /// <paramref name="vocabulary"/> followed by the names <paramref name="later"/> adds — a name already in
    /// the vocabulary keeps its spelling and specimens.
    /// </summary>
    public static IReadOnlyList<KnownType> Extend(IReadOnlyList<KnownType> vocabulary, IReadOnlyList<KnownType> later) =>
        [.. vocabulary, .. later.Where(t => vocabulary.All(v => !TypeNames.SameType(v.Name, t.Name)))];

    /// <summary>
    /// The vocabulary as one call can take it: a specimen that is one of the call's own records is left out
    /// (a record is either new or already known), the name stays.
    /// </summary>
    public static IReadOnlyList<KnownType> For(IReadOnlyList<KnownType> vocabulary, IReadOnlyList<RawRecord> callRecords)
    {
        var own = callRecords.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        return vocabulary.Select(t => t with { Specimens = t.Specimens.Where(r => !own.Contains(r.Id)).ToList() }).ToList();
    }

    /// <summary>
    /// The type names each entity name was written under across <paramref name="proposals"/> — entity names
    /// and type names both compared leniently (case and separators set aside), each type kept in the first
    /// spelling seen.
    /// </summary>
    public static Dictionary<string, HashSet<string>> TypesByName(IEnumerable<OntologyProposal> proposals)
    {
        var types = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var entity in proposals.SelectMany(p => p.Entities))
        {
            var key = Lenient(entity.Name);
            if (!types.TryGetValue(key, out var set))
            {
                types[key] = set = [];
            }

            if (set.All(t => !TypeNames.SameType(t, entity.EntityType)))
            {
                set.Add(entity.EntityType.Trim());
            }
        }

        return types;
    }

    /// <summary>
    /// Of the entity names both runs wrote, how many were written under the same type names in both — the
    /// typing that does not depend on which source came first.
    /// </summary>
    public static (int Alike, int Shared) TypedAlike(Dictionary<string, HashSet<string>> a, Dictionary<string, HashSet<string>> b)
    {
        var shared = a.Keys.Where(b.ContainsKey).ToList();
        return (shared.Count(k => a[k].Count == b[k].Count && a[k].All(t => b[k].Any(u => TypeNames.SameType(t, u)))), shared.Count);
    }

    private static string Lenient(string name) => string.Concat(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant));
}
