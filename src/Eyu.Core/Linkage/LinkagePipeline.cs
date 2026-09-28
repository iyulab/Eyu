using Eyu.Core.Records;

namespace Eyu.Core.Linkage;

/// <summary>
/// Orchestrates <see cref="FieldComparator"/> -&gt; <see cref="FellegiSunterEstimator"/> -&gt;
/// <see cref="LinkageClassifier"/> -&gt; <see cref="EntityClusterer"/> into one
/// <see cref="LinkageAnalysis"/> per record batch, with <see cref="LinkageErrorRateEstimator"/>
/// reporting what the fitted model expects its own classification to have got wrong. Fewer than two records has nothing to compare,
/// so the pipeline is skipped entirely and every record becomes its own singleton cluster — and so
/// is a batch whose records do not denote entities (<see cref="LinkageOptions.RecordsDenoteEntities"/>),
/// where comparing them would answer a question the records never posed.
/// <c>options</c> defaults to <see cref="LinkageOptions.Default"/> — matching every
/// tuning value <see cref="LinkageClassifier"/> and <see cref="FellegiSunterEstimator"/> already
/// used, so a caller that never supplies options sees no behavior change. Every pair of records
/// is compared — there is no blocking or indexing — so the cost is quadratic in the batch size;
/// the catalogues this has been measured on are small, and a large batch is the caller's to
/// pre-partition.
/// </summary>
public static class LinkagePipeline
{
    /// <summary>
    /// A record id names one record; two records under one id would collapse into one cluster
    /// member and confuse every pair that cites it. Refused up front, naming the ids, rather than
    /// as a dictionary collision somewhere inside the clusterer.
    /// </summary>
    internal static void ThrowIfDuplicateIds(IReadOnlyList<string> recordIds)
    {
        var duplicated = recordIds
            .GroupBy(id => id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (duplicated.Count > 0)
        {
            throw new ArgumentException(
                $"Record ids must be unique within a batch; duplicated: {string.Join(", ", duplicated)}.",
                nameof(recordIds));
        }
    }

    public static LinkageAnalysis Analyze(IReadOnlyList<RawRecord> records, LinkageOptions? options = null)
        => Analyze(records, [], options);

    /// <summary>
    /// The same analysis, also comparing each record with the records of entities earlier calls
    /// identified. A known entity's records are compared with the call's records and never with each
    /// other — the caller already decided they are one entity — and they join the call's own pairs in one
    /// parameter estimate, so the evidence against known records is on the same scale as the rest. The
    /// clusters and pair linkages stay the call's own; the cross-call evidence is
    /// <see cref="LinkageAnalysis.KnownCandidates"/>.
    /// </summary>
    public static LinkageAnalysis Analyze(IReadOnlyList<RawRecord> records, IReadOnlyList<KnownEntity> knownEntities, LinkageOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(knownEntities);

        var opts = options ?? LinkageOptions.Default;
        opts.Validate();

        var recordIds = records.Select(r => r.Id).ToList();
        ThrowIfDuplicateIds(recordIds);

        // Where records do not each denote one entity, no record can be the same thing as a known one.
        var knownRecords = opts.RecordsDenoteEntities
            ? knownEntities.SelectMany(k => (k.DenotingRecords ?? []).Select(r => (Key: k.Key, Record: r))).ToList()
            : [];

        var ownPairs = new List<(RawRecord A, RawRecord B)>();
        if (records.Count >= 2 && opts.RecordsDenoteEntities)
        {
            for (var i = 0; i < records.Count; i++)
            {
                for (var j = i + 1; j < records.Count; j++)
                {
                    ownPairs.Add((records[i], records[j]));
                }
            }
        }

        var crossPairs = records.SelectMany(r => knownRecords.Select(k => (Record: r, k.Key, Known: k.Record))).ToList();

        if (ownPairs.Count == 0 && crossPairs.Count == 0)
        {
            var singletonClusters = recordIds.Select(id => new RecordCluster([id])).ToList();
            return new LinkageAnalysis(new ClusteringResult(singletonClusters, []), [], Parameters: null);
        }

        var ownVectors = ownPairs.Select(p => FieldComparator.Compare(p.A, p.B, opts)).ToList();
        var crossVectors = crossPairs.Select(p => FieldComparator.Compare(p.Record, p.Known, opts)).ToList();
        var parameters = FellegiSunterEstimator.Estimate([.. ownVectors, .. crossVectors], opts.MaxIterations, opts.ConvergenceTolerance);

        var pairLinkages = new List<PairLinkage>(ownPairs.Count);
        for (var i = 0; i < ownPairs.Count; i++)
        {
            var llr = FellegiSunterEstimator.ComputeLogLikelihoodRatio(ownVectors[i], parameters);
            pairLinkages.Add(new PairLinkage(ownPairs[i].A.Id, ownPairs[i].B.Id, LinkageClassifier.Classify(llr, opts.MatchThreshold, opts.NonMatchThreshold), llr));
        }

        var knownCandidates = crossPairs
            .Select((p, i) => (p.Record.Id, p.Key, Llr: FellegiSunterEstimator.ComputeLogLikelihoodRatio(crossVectors[i], parameters)))
            .GroupBy(c => (c.Id, c.Key))
            .Select(g => g.MaxBy(c => c.Llr))
            .Select(c => new KnownEntityCandidate(c.Id, c.Key, LinkageClassifier.Classify(c.Llr, opts.MatchThreshold, opts.NonMatchThreshold), c.Llr))
            .Where(c => c.Classification != LinkageClassification.NonMatch)
            .OrderBy(c => c.RecordId, StringComparer.Ordinal)
            .ThenBy(c => c.KnownEntityKey, StringComparer.Ordinal)
            .ToList();

        var clustering = EntityClusterer.Cluster(recordIds, pairLinkages);
        var errorRates = pairLinkages.Count > 0 ? LinkageErrorRateEstimator.Estimate(pairLinkages, parameters) : null;
        return new LinkageAnalysis(clustering, pairLinkages, parameters, errorRates) { KnownCandidates = knownCandidates };
    }
}
