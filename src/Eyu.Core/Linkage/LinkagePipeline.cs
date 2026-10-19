using Eyu.Core.Primitives;
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
        => AnalyzeCore(records, knownEntities, suppliedParameters: null, options);

    /// <summary>
    /// The same analysis, scored with <paramref name="parameters"/> instead of parameters fitted on this
    /// call's pairs — no EM runs. This is the way to ask «is this new record someone we already know»
    /// one record at a time: a call of one record against <c>N</c> known ones holds about one true match
    /// among <c>N</c> pairs, too little for EM to tell what a match looks like, and fitting there reads
    /// whatever most pairs share (a school, a year) as identity. Fit once on the population instead —
    /// <c>u</c> from <see cref="EstimateNonMatchAgreement"/>, <c>m</c> from a reviewed sample or a
    /// fixed-<c>u</c> fit (<see cref="FellegiSunterEstimator.Estimate"/>) — keep the result, and score each
    /// call with it. A field the parameters do not name carries no evidence.
    /// </summary>
    public static LinkageAnalysis Analyze(IReadOnlyList<RawRecord> records, IReadOnlyList<KnownEntity> knownEntities, FieldLinkageParameters parameters, LinkageOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return AnalyzeCore(records, knownEntities, suppliedParameters: parameters, options);
    }

    /// <summary>
    /// Each field's <c>u</c> — how often two records that are <em>not</em> the same entity still agree on
    /// it — measured over pairs of <paramref name="population"/>: every pair when there are at most
    /// <paramref name="maxPairs"/>, otherwise that many pairs drawn at random (deterministic for one
    /// <paramref name="seed"/>). Random pairs of a population are almost all non-matches, so this needs no
    /// EM and is not distorted by how few duplicates there are; it is also what tells a weak field (a
    /// school five hundred pupils share) from a strong one (a name). Compared exactly as
    /// <see cref="FieldComparator.Compare"/> compares under <paramref name="options"/>, and bounded like
    /// every fitted value (<see cref="FellegiSunterEstimator.AgreementProbabilityFloor"/>). A field that is
    /// never present on both sides of a pair is absent from the result.
    /// </summary>
    public static IReadOnlyDictionary<string, double> EstimateNonMatchAgreement(
        IReadOnlyList<RawRecord> population, LinkageOptions? options = null, int maxPairs = DefaultNonMatchSamplePairs, int seed = 0)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPairs, 1);
        var opts = options ?? LinkageOptions.Default;
        opts.Validate();

        var agreed = new Dictionary<string, long>(StringComparer.Ordinal);
        var compared = new Dictionary<string, long>(StringComparer.Ordinal);
        void Count(RawRecord a, RawRecord b)
        {
            foreach (var (field, level) in FieldComparator.Compare(a, b, opts))
            {
                compared[field] = compared.GetValueOrDefault(field) + 1;
                if (level == FieldAgreementLevel.Agree)
                {
                    agreed[field] = agreed.GetValueOrDefault(field) + 1;
                }
            }
        }

        var n = population.Count;
        var allPairs = (long)n * (n - 1) / 2;
        if (allPairs <= maxPairs)
        {
            for (var i = 0; i < n; i++)
            {
                for (var j = i + 1; j < n; j++)
                {
                    Count(population[i], population[j]);
                }
            }
        }
        else
        {
            var random = new Random(seed);
            for (var s = 0; s < maxPairs; s++)
            {
                var i = random.Next(n);
                var j = random.Next(n - 1);
                Count(population[i], population[j >= i ? j + 1 : j]);
            }
        }

        return compared.ToDictionary(
            kv => kv.Key,
            kv => Math.Clamp((double)agreed.GetValueOrDefault(kv.Key) / kv.Value, FellegiSunterEstimator.AgreementProbabilityFloor, FellegiSunterEstimator.AgreementProbabilityCeiling),
            StringComparer.Ordinal);
    }

    /// <summary>The default pair budget of <see cref="EstimateNonMatchAgreement"/>.</summary>
    public const int DefaultNonMatchSamplePairs = 100_000;

    private static LinkageAnalysis AnalyzeCore(IReadOnlyList<RawRecord> records, IReadOnlyList<KnownEntity> knownEntities, FieldLinkageParameters? suppliedParameters, LinkageOptions? options)
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
            foreach (var (i, j) in CandidatePairs(records, records, opts.BlockingRules, sameSide: true))
            {
                ownPairs.Add((records[i], records[j]));
            }
        }

        var knownSide = knownRecords.Select(k => k.Record).ToList();
        var crossPairs = CandidatePairs(records, knownSide, opts.BlockingRules, sameSide: false)
            .Select(p => (Record: records[p.I], knownRecords[p.J].Key, Known: knownRecords[p.J].Record))
            .ToList();

        if (ownPairs.Count == 0 && crossPairs.Count == 0)
        {
            var singletonClusters = recordIds.Select(id => new RecordCluster([id])).ToList();
            return new LinkageAnalysis(new ClusteringResult(singletonClusters, []), [], Parameters: null);
        }

        var ownVectors = ownPairs.Select(p => FieldComparator.Compare(p.A, p.B, opts)).ToList();
        var crossVectors = crossPairs.Select(p => FieldComparator.Compare(p.Record, p.Known, opts)).ToList();
        var parameters = suppliedParameters
            ?? FellegiSunterEstimator.Estimate([.. ownVectors, .. crossVectors], opts.MaxIterations, opts.ConvergenceTolerance);

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

    /// <summary>
    /// The index pairs to compare, in the order every pair would come in without blocking (by the left
    /// index, then the right): all of them when there are no rules, otherwise those sharing a key under
    /// at least one rule. On one side (<paramref name="sameSide"/>) only <c>i &lt; j</c>.
    /// </summary>
    private static IEnumerable<(int I, int J)> CandidatePairs(
        IReadOnlyList<RawRecord> left, IReadOnlyList<RawRecord> right, IReadOnlyList<IReadOnlyList<string>>? rules, bool sameSide)
    {
        if (rules is null || rules.Count == 0)
        {
            for (var i = 0; i < left.Count; i++)
            {
                for (var j = sameSide ? i + 1 : 0; j < right.Count; j++)
                {
                    yield return (i, j);
                }
            }

            yield break;
        }

        var pairs = new HashSet<(int, int)>();
        foreach (var rule in rules)
        {
            var buckets = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (var j = 0; j < right.Count; j++)
            {
                if (BlockingKey(right[j], rule) is { } key)
                {
                    (buckets.TryGetValue(key, out var list) ? list : buckets[key] = []).Add(j);
                }
            }

            for (var i = 0; i < left.Count; i++)
            {
                if (BlockingKey(left[i], rule) is not { } key || !buckets.TryGetValue(key, out var matches))
                {
                    continue;
                }

                foreach (var j in matches)
                {
                    if (!sameSide || i < j)
                    {
                        pairs.Add((i, j));
                    }
                }
            }
        }

        foreach (var pair in pairs.Order())
        {
            yield return pair;
        }
    }

    /// <summary>
    /// A record's key under one blocking rule — its values for the rule's fields, compared as
    /// <see cref="FieldComparator"/> compares exactly — or null when any of them is missing or blank, so
    /// an empty field never puts two records in one block.
    /// </summary>
    private static string? BlockingKey(RawRecord record, IReadOnlyList<string> rule)
    {
        var parts = new string[rule.Count];
        for (var k = 0; k < rule.Count; k++)
        {
            if (!record.Fields.TryGetValue(rule[k], out var value) || string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            parts[k] = TextForm.Fold(value).Trim().ToUpperInvariant();
        }

        return string.Join('\u001F', parts);
    }
}
