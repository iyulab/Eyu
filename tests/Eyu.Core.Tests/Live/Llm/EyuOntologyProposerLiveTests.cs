using Eyu.Core.Judgment;
using Eyu.Core.Records;
using Xunit;

namespace Eyu.Core.Tests.Live.Llm;

/// <summary>
/// The spike's graduation smoke: <see cref="SinglePassOntologyProposer"/> against a real model
/// (any OpenAI-compatible endpoint). The scripted-client suite
/// (<c>SinglePassOntologyProposerTests</c>) pins parsing and prompt construction; what only a live
/// model can answer is whether an actual completion, with all its formatting habits, survives the
/// strict parse and grounds every claim in a source id that was actually given. Records below are a
/// real 3-row slice of <c>league/corpus/nuclear-power/nrc-ler-2020-2026.json</c> (NRC Licensee
/// Event Reports, public data) — declaredStructure is <c>null</c> on purpose: this is raw external
/// data with no Formbase-declared schema behind it, and Eyu only knows what a caller supplies.
/// </summary>
public class EyuOntologyProposerLiveTests
{
    private static readonly RawRecord[] LerRecords =
    [
        new("0252022001", new Dictionary<string, string?>
        {
            ["ler_number"] = "0252022001",
            ["docket"] = "025",
            ["plant_name"] = "Vogtle 3",
            ["event_date"] = "2022-10-06",
            ["title"] = "Automatic Reactor Trip Signal due to Inadequate Procedure Guidance Causing Incorrect Opening of Division B DC Supply Breaker",
        }),
        new("0252022002", new Dictionary<string, string?>
        {
            ["ler_number"] = "0252022002",
            ["docket"] = "025",
            ["plant_name"] = "Vogtle 3",
            ["event_date"] = "2022-10-23",
            ["title"] = "Automatic Depressurization System Stage 4 Flow Paths Inoperable During Mode 6 with Upper Internals in Place due to Inadequate Work Processes",
        }),
        new("0252022003", new Dictionary<string, string?>
        {
            ["ler_number"] = "0252022003",
            ["docket"] = "025",
            ["plant_name"] = "Vogtle 3",
            ["event_date"] = "2022-10-24",
            ["title"] = "Unborated Water Flowpath Not Secured per Technical Specification 3.9.2 due to Inadequate Procedure Revision",
        }),
    ];

    [Fact]
    public async Task A_real_model_proposes_entities_and_relations_grounded_in_the_given_records()
    {
        var (httpClient, modelClient) = EyuLlmLiveClient.Create();
        using var _ = httpClient;
        var proposer = new SinglePassOntologyProposer(modelClient);
        var recordIds = LerRecords.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        var proposal = await proposer.ProposeAsync(declaredStructure: null, LerRecords, TestContext.Current.CancellationToken);

        // The guards make failure loud: a hallucinated field or malformed reply throws
        // (FormatException / ArgumentOutOfRangeException), so reaching here already means the
        // completion survived the strict parse.
        Assert.NotEmpty(proposal.Entities);

        // Grounding integrity is not enforced by GroundedClaim.Create (it only requires a
        // non-empty source list, not that the ids are real) — a source id outside the 3 given
        // records would be a genuine hallucination the parser cannot catch on its own.
        foreach (var entity in proposal.Entities)
        {
            Assert.NotEmpty(entity.Claim.Sources);
            Assert.All(entity.Claim.Sources, source => Assert.Contains(source.RecordId, recordIds));
        }

        foreach (var relation in proposal.Relations)
        {
            Assert.NotEmpty(relation.Claim.Sources);
            Assert.All(relation.Claim.Sources, source => Assert.Contains(source.RecordId, recordIds));
        }
    }
}
