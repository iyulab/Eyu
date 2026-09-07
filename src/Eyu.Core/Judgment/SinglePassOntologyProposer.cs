using System.Globalization;
using System.Text;
using System.Text.Json;
using Eyu.Core.Declared;
using Eyu.Core.Grounding;
using Eyu.Core.Inference;
using Eyu.Core.Linkage;
using Eyu.Core.Ports;
using Eyu.Core.Proposals;
using Eyu.Core.Records;

namespace Eyu.Core.Judgment;

/// <summary>
/// The single-baseline <see cref="IOntologyProposer"/> implementation this library's internal
/// benchmark (a "single baseline" gate) compares against: one prompt, one <see
/// cref="IModelClient"/> call, one parse, with an optional <see cref="LinkageOptions"/> to tune
/// the Fellegi-Sunter pre-filter's classification thresholds and EM iteration limits away from
/// their defaults. Before building the prompt, that pre-filter
/// (<see cref="LinkagePipeline"/>) classifies every record pair as a
/// confirmed match, a confirmed non-match, or a gray-zone case needing the model's judgment.
/// Confirmed matches never use the model's self-reported confidence; gray-zone cases combine the
/// Fellegi-Sunter prior with it via a Bayesian update (<see cref="LinkageConfidenceAdjuster"/>).
/// After the parse, <see cref="DeclaredStructureMerge"/> applies the deterministic half of "Declared
/// always wins": declared types are stamped as such and a relation that misuses a declared name is
/// dropped, so the prompt's authority sentence is a request to the model and the merge is the
/// guarantee to the caller.
/// This class still only proves the wiring is correct; it makes no claim about judgment quality
/// on its own, which no unit test can verify without a real model behind
/// <see cref="IModelClient"/>.
/// </summary>
public sealed class SinglePassOntologyProposer(IModelClient modelClient, LinkageOptions? linkageOptions = null) : IOntologyProposer
{
    private const int DiagnosticExcerptLength = 500;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly LinkageOptions options = linkageOptions ?? LinkageOptions.Default;

    public async Task<OntologyProposal> ProposeAsync(DeclaredStructure? declaredStructure, IReadOnlyList<RawRecord> records, CancellationToken cancellationToken = default)
    {
        var linkageAnalysis = LinkagePipeline.Analyze(records, options);
        var prompt = BuildPrompt(declaredStructure, records, linkageAnalysis);
        var response = await modelClient.CompleteAsync(new ModelRequest(prompt), cancellationToken).ConfigureAwait(false);
        var parsed = ParseResponse(response.Text, linkageAnalysis);
        return DeclaredStructureMerge.Apply(declaredStructure, parsed.Entities, parsed.Relations);
    }

    private static string BuildPrompt(DeclaredStructure? declaredStructure, IReadOnlyList<RawRecord> records, LinkageAnalysis linkageAnalysis)
    {
        var text = new StringBuilder();
        text.AppendLine("Propose entities and relations grounded in the input below.");
        text.AppendLine("Respond with JSON only: {\"entities\":[{\"id\",\"type\",\"claim\",\"sources\",\"origin\",\"confidence\"}],\"relations\":[{\"name\",\"from\",\"to\",\"claim\",\"sources\",\"origin\",\"confidence\"}]}.");
        text.AppendLine("\"origin\" is \"Innate\" or \"Acquired\". Every claim must cite at least one source id.");

        if (declaredStructure is not null)
        {
            text.AppendLine();
            text.AppendLine("Declared structure is authoritative: a declared field or relation is fact, not a hypothesis. Propose a relation a declared relation describes under its declared name, never contradict declared structure, and infer only what nothing declares.");
            text.AppendLine(CultureInfo.InvariantCulture, $"Declared fields for {declaredStructure.Subject}: {string.Join(", ", declaredStructure.Fields.Select(DescribeField))}");
            foreach (var relation in declaredStructure.Relations)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"Declared relation: {DescribeRelation(relation)}");
            }
        }

        text.AppendLine();
        text.AppendLine("Records:");
        foreach (var record in records)
        {
            var fields = string.Join(", ", record.Fields.Select(f => $"{f.Key}={f.Value}"));
            text.AppendLine(CultureInfo.InvariantCulture, $"- {record.Id}: {fields}");
        }

        var linkedClusters = linkageAnalysis.Clustering.Clusters.Where(c => c.RecordIds.Count > 1).ToList();
        if (linkedClusters.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Pre-linked record groups (record linkage already confirmed these denote the same entity -- merge them, do not re-decide):");
            foreach (var cluster in linkedClusters)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- {string.Join(", ", cluster.RecordIds)}");
            }
        }

        if (linkageAnalysis.Clustering.GrayZonePairs.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Ambiguous record pairs needing your judgment (prior evidence toward same entity, in log-odds -- positive favors same entity, negative favors different entities):");
            foreach (var pair in linkageAnalysis.Clustering.GrayZonePairs)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- {pair.RecordIdA} vs {pair.RecordIdB}: prior log-odds {pair.LogLikelihoodRatio:F2}");
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// "qty (integer, required; monetary amount)" — every declared fact the caller supplied, so the
    /// model is told what is declared rather than left to re-infer it from a sample.
    /// </summary>
    private static string DescribeField(DeclaredField field)
    {
        var facts = new List<string>();
        if (field.Kind is { } kind)
        {
            facts.Add(Describe(kind));
        }

        if (field.Required == true)
        {
            facts.Add("required");
        }

        var detail = string.Join(", ", facts);
        if (!string.IsNullOrWhiteSpace(field.SemanticHint))
        {
            detail = detail.Length == 0 ? field.SemanticHint : $"{detail}; {field.SemanticHint}";
        }

        return detail.Length == 0 ? field.Name : $"{field.Name} ({detail})";
    }

    private static string Describe(DeclaredValueKind kind) => kind switch
    {
        DeclaredValueKind.Text => "text",
        DeclaredValueKind.WholeNumber => "integer",
        DeclaredValueKind.FractionalNumber => "decimal",
        DeclaredValueKind.Boolean => "boolean",
        DeclaredValueKind.Timestamp => "timestamp",
        DeclaredValueKind.Identifier => "identifier",
        DeclaredValueKind.Structured => "structured",
        _ => kind.ToString().ToLowerInvariant(),
    };

    /// <summary>"asset -> asset (reference via asset_tag)" — which field carries the relation, and on which side.</summary>
    private static string DescribeRelation(DeclaredRelation relation)
    {
        var facts = new List<string>();
        if (relation.Kind is { } kind)
        {
            facts.Add(kind.ToString().ToLowerInvariant());
        }

        if (!string.IsNullOrWhiteSpace(relation.ViaField))
        {
            facts.Add($"via {relation.ViaField}");
        }

        var head = $"{relation.Name} -> {relation.Target}";
        return facts.Count == 0 ? head : $"{head} ({string.Join(" ", facts)})";
    }

    private static OntologyProposal ParseResponse(string responseText, LinkageAnalysis linkageAnalysis)
    {
        ProposalResponse parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ProposalResponse>(responseText, JsonOptions)
                ?? new ProposalResponse(null, null);
        }
        catch (JsonException ex)
        {
            throw new FormatException(
                $"The model response was not valid JSON. Response text: {Excerpt(responseText)}",
                ex);
        }

        var entities = (parsed.Entities ?? [])
            .Select(e =>
            {
                var claim = ToClaim(e.Claim, e.Sources);
                var citedRecordIds = claim.Sources.Select(s => s.RecordId).Distinct().ToList();
                var confidence = LinkageConfidenceAdjuster.AdjustConfidence(citedRecordIds, e.Confidence, linkageAnalysis);
                return EntityProposal.Create(e.Id, e.Type, claim, Enum.Parse<VocabularyOrigin>(e.Origin), confidence);
            })
            .ToList();

        var relations = (parsed.Relations ?? [])
            .Select(r => RelationProposal.Create(r.Name, r.From, r.To, ToClaim(r.Claim, r.Sources), Enum.Parse<VocabularyOrigin>(r.Origin), r.Confidence))
            .ToList();

        return new OntologyProposal(entities, relations);
    }

    private static GroundedClaim ToClaim(string claim, IReadOnlyList<string> sources) =>
        GroundedClaim.Create(claim, sources.Select(id => new SourceRef(id)).ToList());

    /// <summary>
    /// Bounded excerpt of what the model actually returned. A model that wraps its JSON in prose
    /// or a markdown fence fails here, and without the text the caller cannot tell that apart from
    /// a truncated or refused completion — the text is already in hand, so the exception carries
    /// it rather than discarding it.
    /// </summary>
    private static string Excerpt(string text) => text.Length switch
    {
        0 => "(empty)",
        <= DiagnosticExcerptLength => text,
        _ => $"{text[..DiagnosticExcerptLength]}… ({text.Length} chars total)",
    };

    private sealed record ProposalResponse(IReadOnlyList<EntityResponse>? Entities, IReadOnlyList<RelationResponse>? Relations);

    private sealed record EntityResponse(string Id, string Type, string Claim, IReadOnlyList<string> Sources, string Origin, double Confidence);

    private sealed record RelationResponse(string Name, string From, string To, string Claim, IReadOnlyList<string> Sources, string Origin, double Confidence);
}
