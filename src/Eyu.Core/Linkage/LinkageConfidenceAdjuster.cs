namespace Eyu.Core.Linkage;

/// <summary>
/// Turns a parsed entity's cited record ids + the model's raw self-reported confidence into the
/// confidence <c>SinglePassOntologyProposer</c> actually assigns. A claim citing a confirmed
/// <see cref="LinkageClassification.Match"/> cluster never uses the model's number at all — the
/// whole point of the pre-filter is that clear cases don't depend on self-reported confidence
/// (design rationale §D). A claim touching a <see cref="LinkageClassification.GrayZone"/> or
/// <see cref="LinkageClassification.NonMatch"/> pair combines the Fellegi-Sunter prior log-odds
/// with the model's confidence via a Bayesian update.
/// </summary>
public static class LinkageConfidenceAdjuster
{
    private const double ProbabilityFloor = 1e-6;
    private const double ProbabilityCeiling = 1.0 - 1e-6;

    public static double AdjustConfidence(IReadOnlyList<string> citedRecordIds, double llmConfidence, LinkageAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(citedRecordIds);
        ArgumentNullException.ThrowIfNull(analysis);

        if (citedRecordIds.Count <= 1)
        {
            return llmConfidence;
        }

        var relevantPairs = new List<PairLinkage>();
        for (var i = 0; i < citedRecordIds.Count; i++)
        {
            for (var j = i + 1; j < citedRecordIds.Count; j++)
            {
                var pair = analysis.PairLinkages.FirstOrDefault(p =>
                    (p.RecordIdA == citedRecordIds[i] && p.RecordIdB == citedRecordIds[j]) ||
                    (p.RecordIdA == citedRecordIds[j] && p.RecordIdB == citedRecordIds[i]));
                if (pair is not null)
                {
                    relevantPairs.Add(pair);
                }
            }
        }

        if (relevantPairs.Count == 0)
        {
            return llmConfidence;
        }

        if (relevantPairs.All(p => p.Classification == LinkageClassification.Match))
        {
            return Sigmoid(relevantPairs.Min(p => p.LogLikelihoodRatio));
        }

        var priorLogOdds = relevantPairs.Min(p => p.LogLikelihoodRatio);
        var llmLogOdds = Logit(Math.Clamp(llmConfidence, ProbabilityFloor, ProbabilityCeiling));
        return Sigmoid(priorLogOdds + llmLogOdds);
    }

    private static double Sigmoid(double logOdds) => 1.0 / (1.0 + Math.Exp(-logOdds));

    private static double Logit(double probability) => Math.Log(probability / (1.0 - probability));
}
