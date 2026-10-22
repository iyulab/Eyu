using System.Text.Json;
using Eyu.Core.Declared;
using Eyu.Core.Primitives;
using Eyu.Core.Proposals;
using Eyu.Core.Records;

namespace Eyu.Core.Tests.Quality;

/// <summary>
/// The LLMs4OL 2026 Reuse task (ontology extension) turned into what Eyu reads, and Eyu's answer turned
/// back into the triples the task's scorer reads. A sample hands a text, a small ontology to extend
/// (<c>initial-primitive-ontology-triples</c>), and the vocabulary in play (<c>types</c>, <c>terms</c>); it is
/// scored on the triples it adds — <c>is-a</c> between types, <c>instance-of</c> from a term to its type,
/// and other relations.
/// <para>
/// On Eyu's side the initial ontology is the declaration: an <c>is-a</c> triple declares its subject a kind
/// of its object (<see cref="DeclaredStructure.SuperTypes"/>), any other relation is declared on its subject
/// towards its object, and an <c>instance-of</c> triple's type is a type in use. The sample's types are
/// types in use too (<see cref="KnownType"/>); its terms have no channel of their own — they are in the
/// text. The text is one record of the document-fragment kind. Proposals come back as triples: an inferred
/// type relation as <c>is-a</c>, an entity one of the sample's terms names — in the term's spelling — as <c>instance-of</c> its type (the
/// task's typing step is of the terms it gives — the first pilot also typed every other entity the text named,
/// and with no term to type none was right), a relation under a declared name
/// as itself. What the initial ontology already held is not an extension and is left out.
/// </para>
/// <para>
/// Two outputs, fixed before any run: everything (<see cref="Variant.All"/>), and only triples whose type end
/// is in the sample's vocabulary (<see cref="Variant.Vocabulary"/>) — the sample's types and every name its
/// initial triples use. Eyu types an entity under a name of its own when no name in use fits, which the
/// task scores as a miss; a consumer extending a given ontology keeps to its names.
/// </para>
/// </summary>
internal static class Llms4OlReuseCorpus
{
    public const string IsA = "is-a";
    public const string InstanceOf = "instance-of";

    public enum Variant
    {
        All,
        Vocabulary,
    }

    public sealed record Sample(
        string Id,
        string Context,
        IReadOnlyList<string> Terms,
        IReadOnlyList<string> Types,
        IReadOnlyList<string[]> Initial,
        IReadOnlyList<string[]> Extended);

    public static IReadOnlyList<Sample> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray().Select(e => new Sample(
            e.GetProperty("id").GetString()!,
            e.GetProperty("context").GetString()!,
            Strings(e, "terms"),
            Strings(e, "types"),
            Triples(e, "initial-primitive-ontology-triples"),
            Triples(e, "extended-primitive-ontology-triples"))).ToList();
    }

    /// <summary>The training split writes relation names with spaces (<c>disjoint with</c>), the test split with hyphens — hyphens here.</summary>
    public static string Relation(string name) => string.Join('-', name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The sample's initial ontology as declarations — one per subject, each name spelled as first written.</summary>
    public static IReadOnlyList<DeclaredStructure> Declarations(Sample sample)
    {
        var bySubject = new Dictionary<string, (string Name, List<string> Supers, List<DeclaredRelation> Relations)>(StringComparer.Ordinal);
        foreach (var t in sample.Initial)
        {
            var (s, p, o) = (t[0].Trim(), t[1], t[2].Trim());
            if (p == InstanceOf || Spelling(s).Length == 0 || Spelling(o).Length == 0 || TypeNames.SameType(s, o))
            {
                continue;
            }

            var key = Spelling(s);
            if (!bySubject.TryGetValue(key, out var entry))
            {
                entry = (s, [], []);
                bySubject[key] = entry;
            }

            if (p == IsA)
            {
                if (!entry.Supers.Any(x => TypeNames.SameType(x, o)))
                {
                    entry.Supers.Add(o);
                }
            }
            else if (!entry.Relations.Any(r => r.Name == p && TypeNames.SameType(r.Target.Value, o)))
            {
                entry.Relations.Add(new DeclaredRelation(p, SubjectRef.Create(o)));
            }
        }

        return bySubject.Values
            .Select(e => new DeclaredStructure(SubjectRef.Create(e.Name), [], e.Relations) { SuperTypes = e.Supers })
            .ToList();
    }

    /// <summary>
    /// The sample's types, and the types its initial <c>instance-of</c> triples name, as types in use — less
    /// any a declaration already names, since those are in use already.
    /// </summary>
    public static PriorKnowledge Prior(Sample sample, IReadOnlyList<DeclaredStructure> declarations)
    {
        var declared = declarations.Select(d => Spelling(d.Subject.Value)).ToHashSet(StringComparer.Ordinal);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var type in sample.Types.Concat(sample.Initial.Where(t => t[1] == InstanceOf).Select(t => t[2])).Select(n => n.Trim()))
        {
            var key = Spelling(type);
            if (key.Length > 0 && !declared.Contains(key))
            {
                names.TryAdd(key, type);
            }
        }

        return new PriorKnowledge([], [], names.Values.Select(n => new KnownType(n)).ToList());
    }

    /// <summary>The sample's text as the record a proposer reads.</summary>
    public static RawRecord Record(Sample sample) => new(sample.Id, new Dictionary<string, string?> { ["text"] = sample.Context });

    /// <summary>The triples a proposal adds to the sample's ontology, for the given output.</summary>
    public static List<string[]> Extension(Sample sample, OntologyProposal proposal, Variant variant)
    {
        var initial = sample.Initial.Select(t => (Key(t[0]), t[1], Key(t[2]))).ToHashSet();
        var vocabulary = sample.Types.Concat(sample.Initial.SelectMany(t => new[] { t[0], t[2] })).Select(Key).ToHashSet(StringComparer.Ordinal);
        var declaredNames = sample.Initial.Select(t => t[1]).Where(p => p != IsA && p != InstanceOf).ToHashSet(StringComparer.Ordinal);
        var triples = new List<string[]>();

        void Add(string s, string p, string o, string typeEnd)
        {
            if (initial.Contains((Key(s), p, Key(o)))
                || (variant == Variant.Vocabulary && !vocabulary.Contains(Key(typeEnd)))
                || triples.Any(t => Key(t[0]) == Key(s) && t[1] == p && Key(t[2]) == Key(o)))
            {
                return;
            }

            triples.Add([s, p, o]);
        }

        foreach (var t in proposal.TypeRelations.Where(t => t.Basis == ProposalBasis.Inferred))
        {
            Add(t.SubType, IsA, t.SuperType, t.SuperType);
        }

        // A term names an entity by Eyu's own sameness for names: the task writes its terms with separators of
        // its own («mm topic 8360» for the text's «mm_topic_8360», «a adm1h» for «A.ADM1H»), so the triple
        // keeps the sample's spelling of the term — the vocabulary the consumer extends.
        var terms = sample.Terms.Where(t => Spelling(t).Length > 0).GroupBy(Spelling, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var e in proposal.Entities)
        {
            if (terms.TryGetValue(Spelling(e.Name), out var term))
            {
                Add(term, InstanceOf, e.EntityType, e.EntityType);
            }
        }

        var names = proposal.Entities.GroupBy(e => e.EntityId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);
        foreach (var r in proposal.Relations.Where(r => declaredNames.Contains(r.RelationName)))
        {
            if (names.TryGetValue(r.FromEntityId, out var from) && names.TryGetValue(r.ToEntityId, out var to))
            {
                Add(from, r.RelationName, to, to);
            }
        }

        return triples;
    }

    /// <summary>
    /// One line per sample — <c>{"id", "predicted", "gold"}</c> — for the scorer runner, which scores the
    /// concatenated triples with the task's <c>graph_similarity.evaluate</c>.
    /// </summary>
    public static string ToJsonLine(Sample sample, IReadOnlyList<string[]> predicted) =>
        JsonSerializer.Serialize(new Dictionary<string, object> { ["id"] = sample.Id, ["predicted"] = predicted, ["gold"] = sample.Extended }) + "\n";

    // Eyu's own sameness for type names — case and every character that is not a letter or digit aside
    // (TypeNames.SameType): one declaration per subject, one known type per name, by that rule.
    private static string Spelling(string name) => string.Concat(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant));

    // The scorer's sameness (graph_similarity.normalize): lower case, whitespace collapsed.
    private static string Key(string name) => string.Join(' ', name.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static List<string> Strings(JsonElement e, string name) =>
        e.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList()
            : [];

    private static List<string[]> Triples(JsonElement e, string name) =>
        e.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Select(t => t.EnumerateArray().Select(x => x.GetString() ?? "").ToArray())
                .Where(t => t.Length == 3)
                .Select(t => new[] { t[0], Relation(t[1]), t[2] })
                .ToList()
            : [];
}
