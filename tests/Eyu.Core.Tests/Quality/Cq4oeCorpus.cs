using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Eyu.Core.Records;

namespace Eyu.Core.Tests.Quality;

/// <summary>
/// A dataset of the cq4oe benchmark (oeg-upm/cq4oe-benchmark, Apache-2.0) turned into a case Eyu can be
/// measured on. The benchmark's task reads competency questions and writes an ontology; Eyu reads records,
/// so what carries over is the gold annotation, not the task: the gold ontology's individuals become the
/// records, and each question's gold terms (classes and properties) become the concepts its reach is
/// scored on.
/// <para>
/// An individual's <c>rdf:type</c> is left out of its record. A record that said what class it is would
/// hand the gold class name to the model, and the measurement would report that the data contains the
/// answer — the type is what Eyu is to propose. The other statements stay: a property is a column a
/// source system would have, its value a cell.
/// </para>
/// <para>
/// Some gold terms no proposal from records can reach whatever the model does, and they are reported
/// apart rather than counted as misses (<see cref="CeilingReason"/>): a datatype property needs a literal
/// channel Eyu's proposal does not have, and a class no individual is typed by directly exists only in
/// the class hierarchy, which a proposal does not carry.
/// </para>
/// </summary>
internal sealed record Cq4oeCase(string Name, RawRecord[] Records, Cq4oeQuestion[] Questions, IReadOnlyDictionary<string, CeilingReason> Ceiling)
{
    /// <summary>The datasets whose gold ontology has individuals to turn into records — the others are a TBox alone.</summary>
    public static readonly string[] Datasets = ["wine", "water"];

    /// <summary>
    /// The cases a checkout of the benchmark gives, read from <c>EYU_CQ4OE_ROOT</c>; none when it is unset —
    /// the data lives outside the repository, and a run without it measures the built-in catalog alone.
    /// </summary>
    public static IReadOnlyList<Cq4oeCase> FromEnvironment() =>
        Environment.GetEnvironmentVariable("EYU_CQ4OE_ROOT") is { Length: > 0 } root
            ? [.. Datasets.Select(d => Load(root, d))]
            : [];

    /// <summary>
    /// Each gold question twice, as the catalog scores questions: over all its terms (what the benchmark
    /// scores) and over the terms short of the ceiling, marked as such — so one model call answers both.
    /// </summary>
    public CompetencyQuestion[] CatalogQuestions() =>
    [
        .. Questions.SelectMany(q => new[]
        {
            All(q) is { } all ? new CompetencyQuestion($"{q.Id} {q.Question}", all.RequiredConcepts) : null,
            Reachable(q) is { } reachable && reachable.RequiredConcepts.Length < q.Terms.Count() ? new CompetencyQuestion($"{q.Id} {q.Question} (short of the ceiling)", reachable.RequiredConcepts) : null,
        }).OfType<CompetencyQuestion>(),
    ];

    /// <summary>Where the gold files of one dataset sit under a checkout of the benchmark.</summary>
    public static Cq4oeCase Load(string checkoutRoot, string dataset)
    {
        var owl = Path.Combine(checkoutRoot, "CQ2Onto", "00_gold_standard", dataset, "ontology", $"sub_{dataset}.owl");
        var terms = Path.Combine(checkoutRoot, "CQ2Term", "00_gold_standard", dataset, $"cq_to_terms_{dataset}.json");
        return FromGold($"cq4oe-{dataset}", XDocument.Load(owl), File.ReadAllText(terms));
    }

    public static Cq4oeCase FromGold(string name, XDocument ontology, string cqToTermsJson)
    {
        var individuals = ontology.Root!.Elements(Owl + "NamedIndividual").Where(e => e.Attribute(Rdf + "about") is not null).ToList();
        var records = individuals.Select(ToRecord).ToArray();
        var datatypeProperties = ontology.Root.Elements(Owl + "DatatypeProperty")
            .Select(e => LocalName((string?)e.Attribute(Rdf + "about")))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var directlyTyped = individuals.SelectMany(i => i.Elements(Rdf + "type"))
            .Select(t => LocalName((string?)t.Attribute(Rdf + "resource")))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        var questions = JsonSerializer.Deserialize<List<GoldQuestion>>(cqToTermsJson, JsonOptions)!
            .Select(q => new Cq4oeQuestion(q.Id, q.Question, [.. q.Classes ?? []], [.. q.Properties ?? []]))
            .ToArray();
        var ceiling = new Dictionary<string, CeilingReason>(StringComparer.Ordinal);
        foreach (var question in questions)
        {
            foreach (var property in question.Properties.Where(datatypeProperties.Contains))
            {
                ceiling[property] = CeilingReason.LiteralValue;
            }

            foreach (var cls in question.Classes.Where(c => !directlyTyped.Contains(c)))
            {
                ceiling[cls] = CeilingReason.HierarchyOnly;
            }
        }

        return new Cq4oeCase(name, records, questions, ceiling);
    }

    /// <summary>
    /// A gold term as a concept <see cref="CompetencyQuestionReach"/> can match: the term in the spellings
    /// a proposal writes type and relation names in — run together (<c>redbordeaux</c>, how
    /// <c>RedBordeaux</c> reads lowered), with underscores and with spaces. A term too short to match
    /// safely as a substring (<see cref="CompetencyQuestion.MinimumAlternativeLength"/>) has no concept.
    /// </summary>
    public static string? Concept(string term)
    {
        var words = Words(term);
        if (string.Concat(words).Length < CompetencyQuestion.MinimumAlternativeLength)
        {
            return null;
        }

        return string.Join('|', new[] { string.Concat(words), string.Join('_', words), string.Join(' ', words) }.Distinct(StringComparer.Ordinal));
    }

    /// <summary>The question as a catalog question over the terms that are not past the ceiling, or null when none is left.</summary>
    public CompetencyQuestion? Reachable(Cq4oeQuestion question)
    {
        var concepts = question.Terms.Where(t => !Ceiling.ContainsKey(t)).Select(Concept).OfType<string>().ToArray();
        return concepts.Length == 0 ? null : new CompetencyQuestion(question.Question, concepts);
    }

    /// <summary>The question over all its gold terms, the ceiling included — what the benchmark itself scores.</summary>
    public static CompetencyQuestion? All(Cq4oeQuestion question)
    {
        var concepts = question.Terms.Select(Concept).OfType<string>().ToArray();
        return concepts.Length == 0 ? null : new CompetencyQuestion(question.Question, concepts);
    }

    private static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private static readonly XNamespace Owl = "http://www.w3.org/2002/07/owl#";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static RawRecord ToRecord(XElement individual)
    {
        var id = LocalName((string?)individual.Attribute(Rdf + "about"))!;
        var fields = new Dictionary<string, string?>(StringComparer.Ordinal) { ["name"] = id };
        foreach (var statement in individual.Elements().Where(e => e.Name != Rdf + "type").GroupBy(e => e.Name.LocalName, StringComparer.Ordinal))
        {
            fields[statement.Key] = string.Join("; ", statement.Select(e => LocalName((string?)e.Attribute(Rdf + "resource")) ?? e.Value.Trim()));
        }

        return new RawRecord(id, fields);
    }

    private static string? LocalName(string? iri) =>
        iri is null ? null : iri[(Math.Max(iri.LastIndexOf('#'), iri.LastIndexOf('/')) + 1)..];

    /// <summary>«RedBordeaux» → red, bordeaux · «hasVintageYear» → has, vintage, year · «water_body» → water, body.</summary>
    internal static string[] Words(string term)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        for (var i = 0; i < term.Length; i++)
        {
            var c = term[i];
            if (!char.IsLetterOrDigit(c))
            {
                Flush();
                continue;
            }

            var startsWord = char.IsUpper(c) && current.Length > 0
                && (char.IsLower(term[i - 1]) || (i + 1 < term.Length && char.IsLower(term[i + 1])));
            if (startsWord)
            {
                Flush();
            }

            current.Append(char.ToLowerInvariant(c));
        }

        Flush();
        return [.. words];

        void Flush()
        {
            if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }
    }

    private sealed record GoldQuestion(string Id, string Question, string[]? Classes, string[]? Properties);
}

/// <summary>One gold question: its id in the benchmark, its text, and the terms the annotators said it needs.</summary>
internal sealed record Cq4oeQuestion(string Id, string Question, string[] Classes, string[] Properties)
{
    public IEnumerable<string> Terms => Classes.Concat(Properties);
}

/// <summary>Why a gold term is past what a proposal from records can reach.</summary>
internal enum CeilingReason
{
    /// <summary>A datatype property: its values are literals, and a proposal has no literal channel.</summary>
    LiteralValue,

    /// <summary>A class no individual is typed by directly: it lives in the class hierarchy, which a proposal does not carry.</summary>
    HierarchyOnly,
}
