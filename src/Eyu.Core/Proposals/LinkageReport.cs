using Eyu.Core.Linkage;

namespace Eyu.Core.Proposals;

/// <summary>
/// What the record-linkage pre-filter contributed to a proposal, and where the model's answer
/// departed from it — so a caller can tell a pre-filter mistake from a model that ignored it.
/// </summary>
/// <param name="Analysis">
/// The pre-filter's full result for the call's records: every pair it compared, the groups it
/// confirmed, and the gray-zone pairs — all of them, including any left out of the prompt.
/// </param>
/// <param name="SplitClusters">
/// Groups the pre-filter confirmed denote one entity (and told the model to merge) that the answer
/// nonetheless spread over two or more entities, by the records each entity says denote it
/// (<see cref="EntityProposal.DenotedBy"/>). Either the pre-filter joined records that are not the
/// same thing — agreement on fields that describe something else, such as a referenced record's
/// values copied into each document, reads as identity — or the model did not follow it; the
/// records decide which. Empty when the answer kept every confirmed group together.
/// </param>
/// <param name="GrayZonePairsOmitted">
/// How many gray-zone pairs <see cref="LinkageOptions.MaxGrayZonePairsInPrompt"/> left out of the
/// prompt. Zero when every pair was put to the model.
/// </param>
public sealed record LinkageReport(
    LinkageAnalysis Analysis,
    IReadOnlyList<RecordCluster> SplitClusters,
    int GrayZonePairsOmitted);
