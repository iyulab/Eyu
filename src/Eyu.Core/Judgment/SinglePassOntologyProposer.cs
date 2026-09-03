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
/// The single-baseline <see cref="IOntologyProposer"/> the internal-organization benchmark
/// (ROADMAP.md — "단일 baseline 대비" A/B/C/D gate) compares against: one prompt, one
/// <see cref="IModelClient"/> call, one parse. Before building the prompt, a Fellegi-Sunter
/// record-linkage pre-filter (<see cref="LinkagePipeline"/>) classifies every record pair as a
/// confirmed match, a confirmed non-match, or a gray-zone case needing the model's judgment — see
/// claudedocs/plans/PLAN-Eyu-2026-09-03-fellegi-sunter-hybrid-routing-design.md. Confirmed matches
/// never use the model's self-reported confidence; gray-zone cases combine the Fellegi-Sunter
/// prior with it via a Bayesian update (<see cref="LinkageConfidenceAdjuster"/>). This class still
/// only proves the wiring is correct; it makes no claim about judgment quality on its own, which
/// no unit test can verify without a real model behind <see cref="IModelClient"/>.
/// </summary>
public sealed class SinglePassOntologyProposer(IModelClient modelClient) : IOntologyProposer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<OntologyProposal> ProposeAsync(DeclaredStructure? declaredStructure, IReadOnlyList<RawRecord> records, CancellationToken cancellationToken = default)
    {
        var linkageAnalysis = LinkagePipeline.Analyze(records);
        var prompt = BuildPrompt(declaredStructure, records, linkageAnalysis);
        var response = await modelClient.CompleteAsync(new ModelRequest(prompt), cancellationToken).ConfigureAwait(false);
        return ParseResponse(response.Text, linkageAnalysis);
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
            text.AppendLine(CultureInfo.InvariantCulture, $"Declared fields for {declaredStructure.Subject}: {string.Join(", ", declaredStructure.Fields.Select(f => f.Name))}");
            foreach (var relation in declaredStructure.Relations)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"Declared relation: {relation.Name} -> {relation.Target}");
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
            throw new FormatException("The model response was not valid JSON.", ex);
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

    private sealed record ProposalResponse(IReadOnlyList<EntityResponse>? Entities, IReadOnlyList<RelationResponse>? Relations);

    private sealed record EntityResponse(string Id, string Type, string Claim, IReadOnlyList<string> Sources, string Origin, double Confidence);

    private sealed record RelationResponse(string Name, string From, string To, string Claim, IReadOnlyList<string> Sources, string Origin, double Confidence);
}
