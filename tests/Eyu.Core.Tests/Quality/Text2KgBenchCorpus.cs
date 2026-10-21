using System.Text.Json;
using Eyu.Core.Declared;
using Eyu.Core.Primitives;
using Eyu.Core.Proposals;
using Eyu.Core.Records;

namespace Eyu.Core.Tests.Quality;

/// <summary>
/// Text2KGBench (Mihindukulasooriya et al., ISWC 2023) turned into what Eyu reads, and Eyu's answer turned
/// back into what the benchmark's own scorer reads. The benchmark hands an ontology — concepts, and
/// relations from a domain concept to a range concept — and sentences; it scores the triples extracted
/// from each sentence. A relation is written as the benchmark's system outputs write it — its label with
/// spaces as underscores (<c>cast_member</c>), the form the scorer's ontology-conformance check compares
/// against (precision and recall ignore the difference). On Eyu's side the ontology is the declaration (one structure per concept, each
/// relation declared on its domain), and a sentence is a record of the document-fragment kind: it names
/// several things and denotes none, so the proposer runs with
/// <see cref="Linkage.LinkageOptions.RecordsDenoteEntities"/> off. A proposed relation becomes the triple
/// <c>[from name, relation, to name]</c> of every sentence its claim cites.
/// <para>
/// Scoring stays the benchmark's — this type writes its input format and nothing else, so the numbers are
/// comparable with the baselines it ships. Two outputs: everything the proposal holds, and only what it
/// bases on the declaration (<see cref="ProposalBasis.Declared"/>) — Eyu returns relations nothing
/// declared as inferred, which the benchmark counts as relation hallucination; a consumer that wants the
/// ontology's relations alone filters on the basis, and the second output is that consumer's.
/// </para>
/// </summary>
internal static partial class Text2KgBenchCorpus
{
    /// <summary>The target a relation with a blank range is declared towards.</summary>
    public const string ValueTarget = "value";

    /// <summary>
    /// The ontology as declarations, and the relations that could not be declared — one whose domain is no
    /// concept of the ontology has no subject to stand on. A range that is no concept still points
    /// somewhere: a quarter of the benchmark's gold triples sit on such relations (a date, a name, a type
    /// the concept list leaves out), so the relation is declared towards the range as written, and towards
    /// <see cref="ValueTarget"/> when the range is blank — dropping them would silently shrink the
    /// declaration. Eyu has no literal channel; a model reaches such a value only as an entity named by it.
    /// </summary>
    public static (IReadOnlyList<DeclaredStructure> Declarations, IReadOnlyList<string> Undeclarable) Declarations(JsonElement ontology)
    {
        var labelById = new Dictionary<string, string>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var concept in ontology.GetProperty("concepts").EnumerateArray())
        {
            var label = concept.GetProperty("label").GetString()!.Trim();
            var id = concept.TryGetProperty("qid", out var qid) ? qid.GetString()! : label;
            labelById[id] = label;
            if (!order.Contains(label, StringComparer.Ordinal))
            {
                order.Add(label);
            }
        }

        var relations = order.ToDictionary(l => l, _ => new List<DeclaredRelation>(), StringComparer.Ordinal);
        var undeclarable = new List<string>();
        foreach (var relation in ontology.GetProperty("relations").EnumerateArray())
        {
            var label = relation.GetProperty("label").GetString()!.Trim();
            if (!labelById.TryGetValue(relation.GetProperty("domain").GetString() ?? "", out var domain))
            {
                undeclarable.Add(label);
                continue;
            }

            var rangeId = relation.GetProperty("range").GetString()?.Trim() ?? "";
            var range = labelById.GetValueOrDefault(rangeId) ?? (rangeId.Length > 0 ? rangeId : ValueTarget);

            if (!relations[domain].Any(r => r.Name == label && r.Target == SubjectRef.Create(range)))
            {
                relations[domain].Add(new DeclaredRelation(label, SubjectRef.Create(range)));
            }
        }

        var declarations = order.Select(l => new DeclaredStructure(SubjectRef.Create(l), [], relations[l])).ToList();
        return (declarations, undeclarable);
    }

    /// <summary>A sentence as the record a proposer reads.</summary>
    public static RawRecord Sentence(string id, string text) => new(id, new Dictionary<string, string?> { ["sentence"] = text });

    /// <summary>
    /// Each sentence's triples, in the scorer's input shape, for every sentence id given — an id no relation
    /// cites gets an empty list, so a sentence the model said nothing about is scored as missed rather
    /// than dropped.
    /// </summary>
    public static Dictionary<string, List<string[]>> Triples(IEnumerable<OntologyProposal> proposals, IEnumerable<string> sentenceIds, bool declaredOnly)
    {
        var triples = sentenceIds.Distinct(StringComparer.Ordinal).ToDictionary(id => id, _ => new List<string[]>(), StringComparer.Ordinal);
        foreach (var proposal in proposals)
        {
            var names = proposal.Entities.GroupBy(e => e.EntityId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);
            foreach (var relation in proposal.Relations)
            {
                if ((declaredOnly && relation.Basis != ProposalBasis.Declared)
                    || !names.TryGetValue(relation.FromEntityId, out var from)
                    || !names.TryGetValue(relation.ToEntityId, out var to))
                {
                    continue;
                }

                var label = SystemLabel(relation.RelationName);
                foreach (var sentence in relation.Claim.Sources.Select(s => s.RecordId).Distinct(StringComparer.Ordinal))
                {
                    if (triples.TryGetValue(sentence, out var list) && !list.Any(t => t[0] == from && t[1] == label && t[2] == to))
                    {
                        list.Add([from, label, to]);
                    }
                }
            }
        }

        return triples;
    }

    /// <summary>One element a proposal left out, with the ends of a rejected relation where its account names them.</summary>
    public sealed record Rejection(string Element, string? Name, string Reason, string Detail, string? From, string? FromType, string? To, string? ToType);

    /// <summary>
    /// What a proposal left out. A rejected relation's ends survive as entities, so its account's
    /// <c>(from -> to)</c> ids resolve to the names and types the model gave them — the fact that tells a
    /// relation typed against the declaration from one the declaration does not cover. An account that
    /// names no ends keeps them null.
    /// </summary>
    public static IEnumerable<Rejection> Rejections(OntologyProposal proposal)
    {
        var entities = proposal.Entities.GroupBy(e => e.EntityId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var rejection in proposal.Rejections)
        {
            var ends = rejection.Element == ProposalElement.Relation ? EndsPattern().Match(rejection.Detail) : null;
            var from = ends is { Success: true } ? entities.GetValueOrDefault(ends.Groups[1].Value) : null;
            var to = ends is { Success: true } ? entities.GetValueOrDefault(ends.Groups[2].Value) : null;
            yield return new Rejection(rejection.Element.ToString(), rejection.Id, rejection.Reason.ToString(), rejection.Detail, from?.Name, from?.EntityType, to?.Name, to?.EntityType);
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\((\S+) -> (\S+)\)")]
    private static partial System.Text.RegularExpressions.Regex EndsPattern();

    /// <summary>A relation label as the benchmark's system outputs write it: spaces as underscores.</summary>
    public static string SystemLabel(string relationName) => relationName.Replace(' ', '_');

    /// <summary>The scorer's system-output file: one <c>{"id", "triples"}</c> line per sentence, in the order given.</summary>
    public static string ToJsonLines(IEnumerable<string> sentenceIds, Dictionary<string, List<string[]>> triples) =>
        string.Concat(sentenceIds.Distinct(StringComparer.Ordinal).Select(id =>
            JsonSerializer.Serialize(new Dictionary<string, object> { ["id"] = id, ["triples"] = triples.GetValueOrDefault(id) ?? [] }) + "\n"));
}
