namespace Eyu.Core.Linkage;

/// <summary>
/// The three strata of <c>docs/clerical-review.md</c> section 2, formed before sampling from what
/// the pipeline already knows. Errors are rare and concentrated, so a simple random sample would
/// spend nearly its whole budget on records nothing was going to get wrong.
/// </summary>
public enum ReviewStratumKind
{
    /// <summary>Predicted cluster of size 1 with no ambiguous pair: near-certain precision, carries the recall risk only.</summary>
    Singleton,

    /// <summary>Predicted cluster of size 2 or more with no ambiguous pair: where a false merge costs most and shows least.</summary>
    CleanMerge,

    /// <summary>At least one of the record's pairs sits in the ambiguous posterior band: highest error density per reviewed record.</summary>
    Contested,
}

/// <summary>
/// Why a <see cref="ReviewSample"/> is not the design the protocol describes. The sample is still
/// returned — a smaller-than-asked-for draw is usable, it just cannot be read as if it were the
/// full design. Same contract as <see cref="LinkageErrorRateCaveat"/>.
/// </summary>
[Flags]
public enum ReviewSampleCaveat
{
    None = 0,

    /// <summary>
    /// A stratum holds no records in this batch, so it is absent from the draw. Not the protocol's
    /// "never let a stratum go unsampled" — there was nothing in it to sample — but the design
    /// degenerated to fewer than three strata and a reader comparing two samples should see that.
    /// </summary>
    StratumEmpty = 1 << 0,

    /// <summary>
    /// A stratum held fewer records than were asked for, so all of it was taken. That stratum is a
    /// census and contributes no sampling variance downstream
    /// (<see cref="ClericalReviewCaveat.StratumIsCensus"/>).
    /// </summary>
    StratumTakenWhole = 1 << 1,

    /// <summary>
    /// The batch had no linkage parameters — fewer than two records, so no pair was ever compared.
    /// Nothing can be contested and every record is a singleton by default, which is arithmetic
    /// rather than evidence that the clustering is clean.
    /// </summary>
    NoParameters = 1 << 2,
}

/// <summary>
/// One stratum as drawn: how many records it holds in this batch, and which of them were selected
/// for review. <paramref name="PopulationSize"/> is the stratum's whole membership, not the
/// selection — it is the <c>N_h</c> the estimate is weighted by, so it must survive the draw.
/// </summary>
public sealed record SampledStratum(
    ReviewStratumKind Kind,
    long PopulationSize,
    IReadOnlyList<string> SelectedRecordIds);

/// <summary>
/// A stratified draw for clerical review, reproducible from <paramref name="Seed"/> — the
/// protocol's report names the draw method and seed, so a sample nobody can redraw does not
/// satisfy it.
/// </summary>
public sealed record ReviewSample(
    IReadOnlyList<SampledStratum> Strata,
    int RequestedPerStratum,
    int Seed,
    ReviewSampleCaveat Caveats)
{
    /// <summary>True when all three strata existed and each gave up as many records as were asked for.</summary>
    public bool IsReliable => Caveats == ReviewSampleCaveat.None;

    /// <summary>How many records a reviewer actually has to look at.</summary>
    public int SelectedCount
    {
        get
        {
            var total = 0;
            foreach (var stratum in Strata)
            {
                total += stratum.SelectedRecordIds.Count;
            }

            return total;
        }
    }
}

/// <summary>
/// One stratum's size and score spread, for sizing the round after a pilot.
/// <paramref name="StandardDeviation"/> is the pilot's own per-record score deviation in that
/// stratum — Neyman allocation needs a variance estimate, and before a pilot there is none.
/// </summary>
public sealed record StratumSpread(long PopulationSize, double StandardDeviation);

/// <summary>
/// Draws the stratified review sample of <c>docs/clerical-review.md</c> section 2 — the half the
/// estimator cannot supply for itself. <see cref="ClericalReviewEstimator"/> turns reviewed scores
/// into a population number; this decides which records get reviewed, which is what makes those
/// weights design weights rather than an afterthought.
/// </summary>
public static class ClericalReviewSampler
{
    /// <summary>
    /// The protocol's pilot size, the low end of its 30–50 band: enough per stratum to get a
    /// standard deviation out of it, which is what the next round's allocation needs.
    /// </summary>
    public const int PilotRecordsPerStratum = 30;

    /// <summary>
    /// Which stratum each record of the batch falls in. Exposed separately from
    /// <see cref="DrawPilot"/> because the strata are a property of the run, not of a draw: a
    /// caller reporting the population line of the protocol's table needs <c>N_h</c> without
    /// sampling anything.
    /// <para>
    /// Contested membership is decided by the <em>posterior</em> band
    /// (<see cref="LinkageErrorRateEstimator.IsAmbiguous"/>), not by
    /// <see cref="LinkageClassification.GrayZone"/> and not by
    /// <see cref="ClusteringResult.GrayZonePairs"/>. The two differ on purpose: the classification
    /// is a threshold on the ratio and drops pairs a chain of matches already resolved, while the
    /// protocol wants every record the fitted model was unsure about, resolved or not.
    /// </para>
    /// </summary>
    public static IReadOnlyDictionary<string, ReviewStratumKind> StratifyRecords(LinkageAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        var contested = new HashSet<string>(StringComparer.Ordinal);
        if (analysis.Parameters is { } parameters)
        {
            foreach (var pair in analysis.PairLinkages)
            {
                if (LinkageErrorRateEstimator.IsAmbiguous(pair, parameters))
                {
                    contested.Add(pair.RecordIdA);
                    contested.Add(pair.RecordIdB);
                }
            }
        }

        var strata = new Dictionary<string, ReviewStratumKind>(StringComparer.Ordinal);
        foreach (var cluster in analysis.Clustering.Clusters)
        {
            foreach (var id in cluster.RecordIds)
            {
                strata[id] = contested.Contains(id)
                    ? ReviewStratumKind.Contested
                    : cluster.RecordIds.Count >= 2
                        ? ReviewStratumKind.CleanMerge
                        : ReviewStratumKind.Singleton;
            }
        }

        return strata;
    }

    /// <summary>
    /// A pilot draw: the same number of records requested from every stratum, because before a
    /// pilot there is no variance to allocate against. Selection within a stratum is simple random
    /// without replacement, seeded — record ids are ordered before the draw so the same seed gives
    /// the same sample regardless of what order the clusters came back in.
    /// <para>
    /// A stratum smaller than <paramref name="perStratum"/> is taken whole rather than skipped:
    /// an unsampled stratum has no estimate and its weight does not leave the population.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">When fewer than one record per stratum is requested.</exception>
    public static ReviewSample DrawPilot(LinkageAnalysis analysis, int perStratum, int seed)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentOutOfRangeException.ThrowIfLessThan(perStratum, 1);

        var strata = StratifyRecords(analysis);
        var caveats = analysis.Parameters is null ? ReviewSampleCaveat.NoParameters : ReviewSampleCaveat.None;
        var random = new Random(seed);
        var drawn = new List<SampledStratum>();

        // Iterated in declaration order so the report reads S1, S2, S3 and the seeded draws are
        // consumed in a fixed order -- a dictionary's order would make the seed meaningless.
        foreach (var kind in Enum.GetValues<ReviewStratumKind>())
        {
            var members = strata
                .Where(entry => entry.Value == kind)
                .Select(entry => entry.Key)
                .Order(StringComparer.Ordinal)
                .ToList();

            if (members.Count == 0)
            {
                caveats |= ReviewSampleCaveat.StratumEmpty;
                continue;
            }

            if (members.Count <= perStratum)
            {
                caveats |= ReviewSampleCaveat.StratumTakenWhole;
                drawn.Add(new SampledStratum(kind, members.Count, members));
                continue;
            }

            drawn.Add(new SampledStratum(kind, members.Count, SelectWithoutReplacement(members, perStratum, random)));
        }

        return new ReviewSample(drawn, perStratum, seed, caveats);
    }

    /// <summary>
    /// Neyman allocation for the round after a pilot: spend the budget in proportion to
    /// <c>N_h · s_h</c>, so a stratum that is large or noisy gets more of it. Every stratum gets at
    /// least one record — the protocol forbids leaving one unsampled — and none is asked for more
    /// records than it holds, so the returned counts can sum to less than
    /// <paramref name="totalBudget"/> when a small stratum's share exceeds its size.
    /// </summary>
    /// <exception cref="ArgumentException">When no strata are given, or a stratum's population is not positive.</exception>
    /// <exception cref="ArgumentOutOfRangeException">When the budget cannot give every stratum one record.</exception>
    public static IReadOnlyList<int> AllocateNeyman(IReadOnlyList<StratumSpread> strata, int totalBudget)
    {
        ArgumentNullException.ThrowIfNull(strata);
        if (strata.Count == 0)
        {
            throw new ArgumentException("Allocation needs at least one stratum.", nameof(strata));
        }

        foreach (var stratum in strata)
        {
            if (stratum.PopulationSize <= 0)
            {
                throw new ArgumentException(
                    $"A stratum claims a population of {stratum.PopulationSize}; a stratum holding no records cannot be allocated to.",
                    nameof(strata));
            }
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(totalBudget, strata.Count);

        double weightTotal = 0;
        var weights = new double[strata.Count];
        for (var i = 0; i < strata.Count; i++)
        {
            // A zero deviation would take a stratum's whole share away, so the floor of one record
            // below does the work instead of a weight the caller cannot see.
            weights[i] = strata[i].PopulationSize * Math.Max(strata[i].StandardDeviation, 0.0);
            weightTotal += weights[i];
        }

        var allocation = new int[strata.Count];
        if (weightTotal <= 0.0)
        {
            // No spread anywhere (a pilot where every record scored the same): nothing recommends
            // one stratum over another, so split the budget by population instead.
            for (var i = 0; i < strata.Count; i++)
            {
                weights[i] = strata[i].PopulationSize;
                weightTotal += weights[i];
            }
        }

        var remainders = new (int Index, double Fraction)[strata.Count];
        var assigned = 0;
        for (var i = 0; i < strata.Count; i++)
        {
            var ideal = totalBudget * weights[i] / weightTotal;
            allocation[i] = (int)Math.Floor(ideal);
            remainders[i] = (i, ideal - allocation[i]);
            assigned += allocation[i];
        }

        // Largest remainder, so the counts sum to the budget instead of to whatever rounding left.
        foreach (var (index, _) in remainders.OrderByDescending(r => r.Fraction).ThenBy(r => r.Index))
        {
            if (assigned >= totalBudget)
            {
                break;
            }

            allocation[index]++;
            assigned++;
        }

        for (var i = 0; i < strata.Count; i++)
        {
            allocation[i] = (int)Math.Clamp(allocation[i], 1, strata[i].PopulationSize);
        }

        return allocation;
    }

    /// <summary>
    /// Partial Fisher-Yates: shuffle only as far as the number needed, which is a simple random
    /// sample without replacement and does not touch the rest of the stratum.
    /// </summary>
    private static List<string> SelectWithoutReplacement(List<string> ordered, int count, Random random)
    {
        var pool = new List<string>(ordered);
        var selected = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var pick = random.Next(i, pool.Count);
            (pool[i], pool[pick]) = (pool[pick], pool[i]);
            selected.Add(pool[i]);
        }

        return selected;
    }
}
