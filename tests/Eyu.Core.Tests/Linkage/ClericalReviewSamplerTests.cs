using Eyu.Core.Linkage;
using Xunit;

namespace Eyu.Core.Tests.Linkage;

public class ClericalReviewSamplerTests
{
    private const int Seed = 20260912;

    private static FieldLinkageParameters Parameters(double matchPrior = 0.5) =>
        new(
            new Dictionary<string, double>(),
            new Dictionary<string, double>(),
            matchPrior,
            EstimationStatus.HeuristicDefault);

    /// <summary>
    /// A log-likelihood ratio of 0 with a prior of 0.5 puts a pair at posterior 0.5 -- the middle
    /// of the ambiguous band. Large magnitudes put it far outside on either side.
    /// </summary>
    private static PairLinkage Pair(string a, string b, double logLikelihoodRatio, LinkageClassification classification) =>
        new(a, b, classification, logLikelihoodRatio);

    private static LinkageAnalysis Analysis(
        IReadOnlyList<RecordCluster> clusters,
        IReadOnlyList<PairLinkage> pairs,
        FieldLinkageParameters? parameters) =>
        new(new ClusteringResult(clusters, []), pairs, parameters);

    [Fact]
    public void A_record_the_model_was_unsure_about_is_contested_whatever_its_cluster_looks_like()
    {
        var analysis = Analysis(
            [new(["a", "b"]), new(["c"]), new(["d"])],
            [
                Pair("a", "b", 8.0, LinkageClassification.Match),     // posterior ~= 1.0
                Pair("c", "d", 0.0, LinkageClassification.GrayZone),  // posterior == 0.5
            ],
            Parameters());

        var strata = ClericalReviewSampler.StratifyRecords(analysis);

        Assert.Equal(ReviewStratumKind.CleanMerge, strata["a"]);
        Assert.Equal(ReviewStratumKind.CleanMerge, strata["b"]);
        Assert.Equal(ReviewStratumKind.Contested, strata["c"]);
        Assert.Equal(ReviewStratumKind.Contested, strata["d"]);
    }

    // The protocol forms strata from the posterior band, not from LinkageClassification: the
    // classification is a threshold on the ratio, and a pair can be called NonMatch while the
    // fitted model is still unsure about it. Getting this backwards would put the highest-error
    // records in the lightest-sampled stratum, which is the one thing the design exists to avoid.
    [Fact]
    public void Contested_follows_the_posterior_band_not_the_classification()
    {
        var analysis = Analysis(
            [new(["a"]), new(["b"])],
            [Pair("a", "b", 0.5, LinkageClassification.NonMatch)],
            Parameters());

        var strata = ClericalReviewSampler.StratifyRecords(analysis);

        Assert.Equal(ReviewStratumKind.Contested, strata["a"]);
        Assert.Equal(ReviewStratumKind.Contested, strata["b"]);
    }

    // Fewer than two records means no pair was ever compared, so "nothing is contested" is
    // arithmetic rather than evidence. The caveat says which of the two it is.
    [Fact]
    public void A_batch_with_no_parameters_says_so_instead_of_reporting_a_clean_clustering()
    {
        var sample = ClericalReviewSampler.DrawPilot(Analysis([new(["a"])], [], null), 5, Seed);

        Assert.True(sample.Caveats.HasFlag(ReviewSampleCaveat.NoParameters));
        Assert.False(sample.IsReliable);
        Assert.Equal(nameof(ReviewStratumKind.Singleton), Assert.Single(sample.Strata).Name);
    }

    [Fact]
    public void The_same_batch_and_seed_draw_the_same_records()
    {
        var analysis = ManySingletons(50);

        var first = ClericalReviewSampler.DrawPilot(analysis, 5, Seed);
        var second = ClericalReviewSampler.DrawPilot(analysis, 5, Seed);
        var other = ClericalReviewSampler.DrawPilot(analysis, 5, Seed + 1);

        Assert.Equal(first.Strata[0].SelectedRecordIds, second.Strata[0].SelectedRecordIds);
        Assert.NotEqual(first.Strata[0].SelectedRecordIds, other.Strata[0].SelectedRecordIds);
        Assert.Equal(Seed, first.Seed);
    }

    [Fact]
    public void A_draw_selects_distinct_records_and_keeps_the_stratums_whole_size()
    {
        var sample = ClericalReviewSampler.DrawPilot(ManySingletons(50), 5, Seed);

        var stratum = Assert.Single(sample.Strata);
        Assert.Equal(5, stratum.SelectedRecordIds.Count);
        Assert.Equal(5, stratum.SelectedRecordIds.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(50L, stratum.PopulationSize);
        Assert.Equal(5, sample.SelectedCount);
    }

    // An unsampled stratum has no estimate and its weight does not leave the population, so a
    // stratum smaller than the request is taken whole rather than skipped -- and flagged, because
    // downstream it becomes a census with no sampling variance.
    [Fact]
    public void A_stratum_smaller_than_the_request_is_taken_whole_and_flagged()
    {
        var sample = ClericalReviewSampler.DrawPilot(ManySingletons(3), 10, Seed);

        var stratum = Assert.Single(sample.Strata);
        Assert.Equal(3, stratum.SelectedRecordIds.Count);
        Assert.True(sample.Caveats.HasFlag(ReviewSampleCaveat.StratumTakenWhole));
    }

    [Fact]
    public void A_stratum_with_no_records_at_all_is_absent_and_flagged()
    {
        var sample = ClericalReviewSampler.DrawPilot(ManySingletons(10), 2, Seed);

        Assert.True(sample.Caveats.HasFlag(ReviewSampleCaveat.StratumEmpty));
        Assert.DoesNotContain(sample.Strata, s => s.Name == nameof(ReviewStratumKind.Contested));
    }

    [Fact]
    public void Asking_for_no_records_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ClericalReviewSampler.DrawPilot(ManySingletons(10), 0, Seed));
    }

    // Neyman: spend in proportion to N_h * s_h. Two strata of 100 and 900, deviations 0.5 and 0.1,
    // budget 100 -> weights 50 and 90 -> 100*50/140 = 35.71 and 100*90/140 = 64.29, floors 35 and
    // 64, one left over to the larger remainder (0.71 > 0.29) -> 36 and 64.
    [Fact]
    public void Allocation_follows_population_times_spread_and_sums_to_the_budget()
    {
        var allocation = ClericalReviewSampler.AllocateNeyman(
            [new StratumSpread(100, 0.5), new StratumSpread(900, 0.3)],
            100);

        // weights 50 : 270 -> 15.6 : 84.4 -> largest remainder gives the second the odd record
        Assert.Equal([16, 84], allocation);
    }

    // A pilot deviation below the design floor is planned against the floor: 0.1 is read as
    // sqrt(0.05 * 0.95) = 0.218, so the large stratum's weight is 900 * 0.218 = 196.1 against
    // 100 * 0.5 = 50, not the 900 * 0.1 = 90 a literal reading would give.
    [Fact]
    public void A_pilot_deviation_below_the_design_floor_is_planned_against_the_floor()
    {
        var allocation = ClericalReviewSampler.AllocateNeyman(
            [new StratumSpread(100, 0.5), new StratumSpread(900, 0.1)],
            100);

        Assert.Equal([20, 80], allocation);
    }

    // The pilot that produced [1, 1, 5]: two strata whose pilot records all scored the same, one
    // contested stratum with some spread, budget 12. Read literally, the zero deviations put the
    // whole budget on the contested stratum, which was capped at its 5 records, and 5 of the 12
    // went nowhere. With the design floor the quiet strata keep a share, and what the capped
    // stratum cannot take is handed on, so the budget is spent.
    [Fact]
    public void A_stratum_with_no_pilot_spread_keeps_a_share_and_a_capped_stratum_hands_its_surplus_on()
    {
        var allocation = ClericalReviewSampler.AllocateNeyman(
            [new StratumSpread(3, 0.0), new StratumSpread(8, 0.0), new StratumSpread(5, 0.14)],
            12);

        Assert.Equal(12, allocation.Sum());
        Assert.Equal([2, 6, 4], allocation);
    }

    [Fact]
    public void A_capped_stratum_passes_its_surplus_to_the_others_until_the_budget_is_spent()
    {
        var allocation = ClericalReviewSampler.AllocateNeyman(
            [new StratumSpread(3, 0.9), new StratumSpread(100, 0.3)],
            50);

        Assert.Equal(3, allocation[0]);
        Assert.Equal(47, allocation[1]);
    }

    [Fact]
    public void A_budget_larger_than_the_population_stops_at_the_population()
    {
        var allocation = ClericalReviewSampler.AllocateNeyman(
            [new StratumSpread(3, 0.5), new StratumSpread(4, 0.5)],
            50);

        Assert.Equal([3, 4], allocation);
    }

    // A noisy small stratum can out-earn a quiet large one -- which is the whole reason to allocate
    // by N*s instead of by N. "Quiet" is read at the design floor (0.218), so the large stratum's
    // weight is 300 * 0.218 = 65 against the small one's 100 * 0.9 = 90.
    [Fact]
    public void A_small_noisy_stratum_outranks_a_large_quiet_one()
    {
        var allocation = ClericalReviewSampler.AllocateNeyman(
            [new StratumSpread(100, 0.9), new StratumSpread(300, 0.01)],
            100);

        Assert.True(allocation[0] > allocation[1], $"got [{allocation[0]}, {allocation[1]}]");
    }

    [Fact]
    public void No_stratum_is_allocated_zero_records_or_more_records_than_it_holds()
    {
        var allocation = ClericalReviewSampler.AllocateNeyman(
            [new StratumSpread(5, 0.0), new StratumSpread(10_000, 0.4)],
            100);

        Assert.True(allocation[0] >= 1, $"first stratum got {allocation[0]}");
        Assert.True(allocation[0] <= 5, $"first stratum got {allocation[0]}, which is more than it holds");
    }

    // A pilot where every record scored the same leaves Neyman with nothing to weigh; every
    // stratum is then planned against the same design floor, which splits the budget by population.
    [Fact]
    public void With_no_spread_anywhere_the_budget_splits_by_population()
    {
        var allocation = ClericalReviewSampler.AllocateNeyman(
            [new StratumSpread(100, 0.0), new StratumSpread(300, 0.0)],
            100);

        Assert.Equal([25, 75], allocation);
    }

    [Fact]
    public void A_budget_too_small_to_give_every_stratum_one_record_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ClericalReviewSampler.AllocateNeyman(
            [new StratumSpread(100, 0.5), new StratumSpread(100, 0.5), new StratumSpread(100, 0.5)],
            2));
    }

    private static LinkageAnalysis ManySingletons(int count) =>
        Analysis(
            [.. Enumerable.Range(0, count).Select(i => new RecordCluster([$"r{i:D3}"]))],
            [],
            Parameters());
}
