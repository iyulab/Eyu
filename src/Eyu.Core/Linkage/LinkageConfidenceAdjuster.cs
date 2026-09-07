namespace Eyu.Core.Linkage;

/// <summary>
/// Turns a parsed entity's cited record ids + the model's raw self-reported confidence into the
/// confidence <c>SinglePassOntologyProposer</c> actually assigns. A claim citing a confirmed
/// <see cref="LinkageClassification.Match"/> cluster never uses the model's number at all — the
/// whole point of the pre-filter is that clear cases don't depend on self-reported confidence
/// (design rationale §D). A claim touching a <see cref="LinkageClassification.GrayZone"/> or
/// <see cref="LinkageClassification.NonMatch"/> pair combines the Fellegi-Sunter prior log-odds
/// with the model's confidence via a Bayesian update. Every posterior computed here folds in
/// <see cref="FieldLinkageParameters.MatchPrior"/> (via <c>Logit</c>) rather than treating the raw
/// log-likelihood ratio as if match and non-match were equally likely a priori.
/// </summary>
public static class LinkageConfidenceAdjuster
{
    private const double ProbabilityFloor = 1e-6;
    private const double ProbabilityCeiling = 1.0 - 1e-6;

    public static double AdjustConfidence(IReadOnlyList<string> citedRecordIds, double llmConfidence, LinkageAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(citedRecordIds);
        ArgumentNullException.ThrowIfNull(analysis);

        if (llmConfidence is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(llmConfidence), llmConfidence, "Confidence must be within [0, 1].");
        }

        if (citedRecordIds.Count <= 1)
        {
            return llmConfidence;
        }

        var citedIds = new HashSet<string>(citedRecordIds, StringComparer.Ordinal);
        var matchPriorLogOdds = analysis.Parameters is null ? 0.0 : Logit(analysis.Parameters.MatchPrior);
        double Posterior(double logLikelihoodRatio) =>
            Math.Clamp(Sigmoid(logLikelihoodRatio + matchPriorLogOdds), ProbabilityFloor, ProbabilityCeiling);

        // A cluster is the transitive closure of Match edges, so a pair inside a confirmed cluster
        // can still read GrayZone on its own -- consulting only PairLinkages would blend the
        // model's confidence back into a group the pre-filter already decided.
        if (analysis.Clustering.Clusters.Any(c => citedIds.IsSubsetOf(c.RecordIds)))
        {
            var directMatchEdges = analysis.PairLinkages
                .Where(p => p.Classification == LinkageClassification.Match &&
                            citedIds.Contains(p.RecordIdA) && citedIds.Contains(p.RecordIdB))
                .ToList();

            if (directMatchEdges.Count > 0)
            {
                return Posterior(directMatchEdges.Min(p => p.LogLikelihoodRatio));
            }
        }

        // One lookup keyed by the unordered pair, instead of a scan of every linkage per cited pair.
        var byPair = new Dictionary<(string, string), PairLinkage>();
        foreach (var pair in analysis.PairLinkages)
        {
            byPair[PairKey(pair.RecordIdA, pair.RecordIdB)] = pair;
        }

        var relevantPairs = new List<PairLinkage>();
        for (var i = 0; i < citedRecordIds.Count; i++)
        {
            for (var j = i + 1; j < citedRecordIds.Count; j++)
            {
                if (byPair.TryGetValue(PairKey(citedRecordIds[i], citedRecordIds[j]), out var pair))
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
            return Posterior(relevantPairs.Min(p => p.LogLikelihoodRatio));
        }

        var priorLogOdds = relevantPairs.Min(p => p.LogLikelihoodRatio) + matchPriorLogOdds;
        var llmLogOdds = Logit(Math.Clamp(llmConfidence, ProbabilityFloor, ProbabilityCeiling));
        return Math.Clamp(Sigmoid(priorLogOdds + llmLogOdds), ProbabilityFloor, ProbabilityCeiling);
    }

    private static (string, string) PairKey(string a, string b) =>
        string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);

    private static double Sigmoid(double logOdds) => 1.0 / (1.0 + Math.Exp(-logOdds));

    private static double Logit(double probability) => Math.Log(probability / (1.0 - probability));
}
