using Eyu.Core.Judgment;
using Eyu.Core.Linkage;
using Eyu.Core.Proposals;
using Eyu.Core.Records;
using Xunit;

namespace Eyu.Core.Tests.Live.Llm;

/// <summary>
/// Live-model regression guard for the notation-variant duplicate-miss defect:
/// three synthetic company records where two (<c>company-a-1</c>/<c>company-a-2</c>) denote the
/// same company under different punctuation/hyphenation, and one (<c>company-b</c>) is a
/// genuinely distinct company. With <see cref="LinkageOptions.UseStringSimilarityComparator"/>
/// enabled, Fellegi-Sunter's field comparison — not the model's judgment — classifies the a-1/a-2
/// pair as Match, and the model is told "merge them, do not re-decide" (README); this checks that
/// a real model actually honors that instruction, not just that the classifier computes it (the
/// classifier half is deterministic and already covered by
/// <c>LinkagePipelineTests.Enabling_string_similarity_flips_classification_for_notation_only_differences</c>,
/// no live model needed there). There is deliberately no "default options leaves them unmerged"
/// counterpart here: a live run during this fix found the model
/// sometimes merges the pair through its own reasoning even without the Fellegi-Sunter hint — not
/// a regression, since Eyu never commits to what the model does absent a hint (README: the
/// pre-filter's classification, not the model's unaided judgment, is what this library
/// guarantees). Asserting "does not merge" there would gate on behavior the system does not
/// promise, which is exactly the over-claiming this project's own README explicitly avoids
/// elsewhere.
/// </summary>
public class FieldComparatorNotationVariantLiveTests
{
    private static readonly RawRecord[] Records =
    [
        new("company-a-1", new Dictionary<string, string?>
        {
            ["name"] = "Nuclear Power Ler Co., Ltd.",
            ["registrationNumber"] = "110111-1234567",
            ["address"] = "Seoul, Gangnam-gu",
        }),
        new("company-a-2", new Dictionary<string, string?>
        {
            ["name"] = "Nuclear Power Ler Co Ltd",
            ["registrationNumber"] = "1101111234567",
            ["address"] = "Seoul Gangnam-gu",
        }),
        new("company-b", new Dictionary<string, string?>
        {
            ["name"] = "Aviation SDR Inc.",
            ["registrationNumber"] = "220222-7654321",
            ["address"] = "Busan, Haeundae-gu",
        }),
    ];

    private static bool AnyEntityMergesBothRecords(OntologyProposal proposal, string recordIdA, string recordIdB) =>
        proposal.Entities.Any(e =>
            e.Claim.Sources.Any(s => s.RecordId == recordIdA) &&
            e.Claim.Sources.Any(s => s.RecordId == recordIdB));

    [Fact]
    public async Task String_similarity_comparison_merges_the_notation_variant_pair()
    {
        var (httpClient, modelClient) = EyuLlmLiveClient.Create();
        using var _ = httpClient;
        var proposer = new SinglePassOntologyProposer(modelClient, new LinkageOptions(UseStringSimilarityComparator: true));

        var proposal = await proposer.ProposeAsync(declaredStructure: null, Records, TestContext.Current.CancellationToken);

        Assert.True(AnyEntityMergesBothRecords(proposal, "company-a-1", "company-a-2"));
    }
}
