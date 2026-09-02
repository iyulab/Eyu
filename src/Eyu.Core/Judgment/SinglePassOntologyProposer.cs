using System.Globalization;
using System.Text;
using System.Text.Json;
using Eyu.Core.Declared;
using Eyu.Core.Grounding;
using Eyu.Core.Inference;
using Eyu.Core.Ports;
using Eyu.Core.Proposals;
using Eyu.Core.Records;

namespace Eyu.Core.Judgment;

/// <summary>
/// The single-baseline <see cref="IOntologyProposer"/> the internal-organization benchmark
/// (ROADMAP.md — "단일 baseline 대비" A/B/C/D gate) compares against: one prompt, one
/// <see cref="IModelClient"/> call, one parse. This class only proves the wiring — prompt
/// construction from declared structure and sampled records, and response parsing back into an
/// <see cref="OntologyProposal"/> — is correct; it makes no claim about judgment quality, which
/// no unit test can verify without a real model behind <see cref="IModelClient"/>. The response
/// parser reuses <see cref="EntityProposal.Create"/>/<see cref="RelationProposal.Create"/>'s own
/// validation rather than re-implementing it, so a malformed proposal from the model fails the
/// same way a hand-built one would.
/// </summary>
public sealed class SinglePassOntologyProposer(IModelClient modelClient) : IOntologyProposer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<OntologyProposal> ProposeAsync(DeclaredStructure? declaredStructure, IReadOnlyList<RawRecord> records, CancellationToken cancellationToken = default)
    {
        var prompt = BuildPrompt(declaredStructure, records);
        var response = await modelClient.CompleteAsync(new ModelRequest(prompt), cancellationToken).ConfigureAwait(false);
        return ParseResponse(response.Text);
    }

    private static string BuildPrompt(DeclaredStructure? declaredStructure, IReadOnlyList<RawRecord> records)
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

        return text.ToString();
    }

    private static OntologyProposal ParseResponse(string responseText)
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
            .Select(e => EntityProposal.Create(e.Id, e.Type, ToClaim(e.Claim, e.Sources), Enum.Parse<VocabularyOrigin>(e.Origin), e.Confidence))
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
