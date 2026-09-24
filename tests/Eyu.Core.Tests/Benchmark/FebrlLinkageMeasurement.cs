using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Eyu.Core.Linkage;
using Eyu.Core.Records;
using Xunit;

namespace Eyu.Core.Tests.Benchmark;

/// <summary>
/// Entity-resolution accuracy on labeled public benchmarks: Febrl's synthetic person records
/// (<see cref="Datasets"/> — an easy set of pairs and a harder set of clusters up to six records),
/// linked by <see cref="LinkagePipeline"/> and scored against each dataset's own cluster labels. Two things are measured, and they answer different questions:
/// <list type="bullet">
/// <item>the <b>census</b> — B-cubed precision, recall and F1 of the whole predicted clustering
/// against the full truth, which is what the linkage achieves on this corpus under this
/// configuration;</item>
/// <item>the <b>clerical review protocol</b> of <c>docs/clerical-review.md</c>, run with the
/// benchmark labels as the reviewer — a pilot draw, per-record scoring, the stratified estimate
/// and its intervals, repeated over many seeds so the report can say how often the 95% interval
/// actually contains the census value. On a corpus without labels that protocol is the only way
/// to a number; here it can be checked against the number it estimates.</item>
/// </list>
/// Beside them, the pre-filter's unlabeled error-rate estimate (<see cref="LinkageErrorRateEstimate"/>)
/// is set against the pair-level rates the labels give, and re-fitted at neighbouring thresholds —
/// the comparison and sensitivity lines of the protocol's report.
/// <para>
/// The dataset is not in this repository. Febrl's data files are licensed under the ANU Open
/// Source License 1.3 (a file-level copyleft), so rather than carry a third-party-licensed file,
/// the measurement downloads it from a fixed commit of a public mirror and refuses it unless its
/// SHA-256 matches. Excluded from the default build (<c>-p:IncludeEyuBenchmarks=true</c>): it needs
/// the network and it measures rather than gates. <c>EYU_BENCHMARK_REPORT</c> names a file for the
/// markdown report; <c>EYU_BENCHMARK_CACHE</c> a directory for the downloaded data (default: the
/// system temp directory).
/// </para>
/// </summary>
public class FebrlLinkageMeasurement(ITestOutputHelper output)
{
    /// <summary>
    /// <c>recordlinkage</c> (BSD-3) ships the Febrl datasets unchanged; this is the last commit that
    /// touched them. The files themselves remain under ANUOS 1.3 — Copyright 2002-2007
    /// Australian National University and others.
    /// </summary>
    private const string MirrorCommit = "8e4a5b1c1e5445a16fa24ad1894ac96e2016798a";

    /// <summary>
    /// One benchmark: which Febrl file, its pinned hash, and which of its entities to keep.
    /// <c>dataset1</c> is taken whole; <c>dataset3</c> (5,000 records, up to five duplicates per
    /// original, up to four modifications in a field and six in a record) is cut to every entity
    /// whose number is a multiple of five — whole clusters only, so the truth stays complete, and
    /// small enough for an all-pairs comparison.
    /// </summary>
    private sealed record Dataset(string File, string Sha256, int EntityModulus, string Description);

    private static readonly Dataset[] Datasets =
    [
        new("dataset1.csv", "637acf9db993a77cc49d479c7c53b739a748615f272a050ff973e8038b1b9cb6", 1,
            "every entity exactly two records (one original, one duplicate with one modified field) — the easy case"),
        new("dataset3.csv", "0e667330458ae88dd3d6b9cab39af4e7629a2fef98a810d0ea5f15e48220bdbf", 5,
            "entities numbered by a multiple of five; zero to five duplicates per original, up to four modifications in a field and six in a record"),
    ];

    private const string Reviewer = "benchmark-labels";

    /// <summary>The protocol's pilot size (section 2: "30–50 reviewed records per stratum").</summary>
    private const int PilotPerStratum = ClericalReviewSampler.PilotRecordsPerStratum;

    /// <summary>Repetitions of the pilot over distinct seeds, for the interval-coverage line.</summary>
    private const int CoverageDraws = 100;

    [Fact]
    public async Task Measure_linkage_accuracy_on_febrl_benchmarks()
    {
        var report = new StringBuilder();
        report.AppendLine("# Entity-resolution accuracy — Febrl (labeled public benchmark)");
        report.AppendLine();
        report.AppendLine("Reviewer: the benchmark's own labels, applied as a blind reviewer would (every candidate of a sampled record judged Same or Different; never *cannot tell*). No human adjudication — a labeled benchmark is the ground truth.");
        report.AppendLine("Blocking: none (all pairs compared). Fields compared: every column except `rec_id` unless a configuration drops some; a blank value is never counted as disagreement.");
        report.AppendLine();

        // With a date of birth and an identity number on every record, an all-pairs comparison over
        // ten fields separates Febrl's duplicates almost perfectly — a ceiling that says the pipeline
        // is correct, not how it degrades, and that leaves the review protocol nothing to estimate.
        // Dropping the two identifiers is the common real case (names and addresses only) and the one
        // where errors appear.
        string[] identifiers = ["date_of_birth", "soc_sec_id"];
        var configurations = new (string Name, LinkageOptions Options, string[] Dropped)[]
        {
            ("exact (default)", LinkageOptions.Default, []),
            ("Jaro-Winkler ≥ 0.90", LinkageOptions.Default with { UseStringSimilarityComparator = true }, []),
            ("exact, without date of birth and identity number", LinkageOptions.Default, identifiers),
            ("Jaro-Winkler ≥ 0.90, without date of birth and identity number", LinkageOptions.Default with { UseStringSimilarityComparator = true }, identifiers),
        };

        foreach (var dataset in Datasets)
        {
            var (records, truth) = Parse(await LoadDatasetAsync(dataset), dataset.EntityModulus);
            report.AppendLine(CultureInfo.InvariantCulture, $"# `{dataset.File}` (Febrl, ANUOS 1.3), SHA-256 `{dataset.Sha256[..12]}…`");
            report.AppendLine();
            report.AppendLine(CultureInfo.InvariantCulture, $"{records.Count} synthetic person records, {truth.Count} true entities ({string.Join(" · ", truth.GroupBy(t => t.RecordIds.Count).OrderBy(g => g.Key).Select(g => $"{g.Count()}×size {g.Key}"))}) — {dataset.Description}.");
            report.AppendLine();

            foreach (var (name, options, dropped) in configurations)
            {
                var compared = dropped.Length == 0
                    ? records
                    : records.Select(r => new RawRecord(r.Id, r.Fields.Where(f => !dropped.Contains(f.Key)).ToDictionary(f => f.Key, f => f.Value))).ToList();
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var analysis = LinkagePipeline.Analyze(compared, options);
                var analysisTime = clock.Elapsed;
                AppendConfiguration(report, $"{dataset.File} — {name}", options, analysis, compared, truth, analysisTime);
            }
        }

        output.WriteLine(report.ToString());
        var reportPath = Environment.GetEnvironmentVariable("EYU_BENCHMARK_REPORT");
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            await File.WriteAllTextAsync(reportPath, report.ToString(), TestContext.Current.CancellationToken);
        }
    }

    private static void AppendConfiguration(
        StringBuilder report,
        string name,
        LinkageOptions options,
        LinkageAnalysis analysis,
        IReadOnlyList<RawRecord> records,
        IReadOnlyList<RecordCluster> truth,
        TimeSpan analysisTime)
    {
        var predicted = analysis.Clustering.Clusters;
        var census = ClusteringMetrics.BCubed(predicted, truth);
        var linked = predicted.Where(c => c.RecordIds.Count >= 2).Sum(c => c.RecordIds.Count);
        var trulyLinked = truth.Where(c => c.RecordIds.Count >= 2).Sum(c => c.RecordIds.Count);
        var strata = ClericalReviewSampler.StratifyRecords(analysis);

        report.AppendLine(CultureInfo.InvariantCulture, $"## {name}");
        report.AppendLine();
        report.AppendLine(CultureInfo.InvariantCulture, $"Thresholds: match ≥ {options.MatchThreshold}, non-match ≤ {options.NonMatchThreshold} (log-likelihood ratio); EM max {options.MaxIterations} iterations, tolerance {options.ConvergenceTolerance}.");
        if (analysis.Parameters is { } parameters)
        {
            var fields = parameters.MAgreeProbability.Keys.Order(StringComparer.Ordinal)
                .Select(f => string.Create(CultureInfo.InvariantCulture, $"{f} m={parameters.MAgreeProbability[f]:0.000} u={parameters.UAgreeProbability[f]:0.000}"));
            report.AppendLine(CultureInfo.InvariantCulture, $"Fitted: status {parameters.Status}, match prior {parameters.MatchPrior:0.00000}; {string.Join(" · ", fields)}.");
        }

        report.AppendLine();
        report.AppendLine("| line | value |");
        report.AppendLine("|---|---|");
        report.AppendLine(CultureInfo.InvariantCulture, $"| Population | N = {records.Count}; {string.Join(" · ", Enum.GetValues<ReviewStratumKind>().Select(k => $"{k} {strata.Values.Count(v => v == k)}"))} |");
        report.AppendLine(CultureInfo.InvariantCulture, $"| **Census B-cubed** (full truth) | precision **{census.Precision:0.0000}** · recall **{census.Recall:0.0000}** · F1 **{census.F1:0.0000}** |");
        report.AppendLine(CultureInfo.InvariantCulture, $"| Linkage rate | {linked}/{records.Count} = {(double)linked / records.Count:0.000} in a cluster of ≥ 2 (truth: {(double)trulyLinked / records.Count:0.000}); predicted clusters {predicted.Count} (truth {truth.Count}) · gray-zone pairs {analysis.Clustering.GrayZonePairs.Count} |");

        var clock = System.Diagnostics.Stopwatch.StartNew();
        AppendReviewLines(report, analysis, truth, census);
        var reviewTime = clock.Elapsed;
        clock.Restart();
        AppendPairLines(report, options, analysis, records, truth);
        var pairTime = clock.Elapsed;

        // Where a run's time goes, so that a slow run says what to make faster. Not comparable
        // across machines; everything above this line is deterministic, this line is not.
        report.AppendLine(CultureInfo.InvariantCulture, $"| Run cost | linkage {analysisTime.TotalSeconds:0}s · review simulation ({CoverageDraws} pilots) {reviewTime.TotalSeconds:0}s · comparison and sensitivity (4 re-fits) {pairTime.TotalSeconds:0}s |");
        report.AppendLine();
    }

    /// <summary>One pilot as the report's Sample/Scores lines, then the same pilot over many seeds for coverage.</summary>
    private static void AppendReviewLines(StringBuilder report, LinkageAnalysis analysis, IReadOnlyList<RecordCluster> truth, ClusteringScore census)
    {
        var clusterOf = ClusterIndex(truth);

        var first = RunPilot(analysis, clusterOf, seed: 1);
        report.AppendLine(CultureInfo.InvariantCulture, $"| Sample | pilot, {PilotPerStratum} per stratum, simple random without replacement, seed 1: {string.Join(" · ", first.Sample.Strata.Select(s => $"{s.Kind} {s.SelectedRecordIds.Count}/{s.PopulationSize}"))} (caveats: {first.Sample.Caveats}) |");
        report.AppendLine(CultureInfo.InvariantCulture, $"| Scores (pilot estimate, seed 1) | precision {Describe(first.Precision)} · recall {Describe(first.Recall)} |");
        report.AppendLine(CultureInfo.InvariantCulture, $"| Exclusions | {first.Excluded} (the labels never answer *cannot tell*) |");

        int precisionCovered = 0, recallCovered = 0, precisionBootCovered = 0, recallBootCovered = 0, reliable = 0;
        double precisionError = 0, recallError = 0;
        for (var seed = 1; seed <= CoverageDraws; seed++)
        {
            var pilot = seed == 1 ? first : RunPilot(analysis, clusterOf, seed);
            precisionCovered += Covers(pilot.Precision.Normal, census.Precision);
            recallCovered += Covers(pilot.Recall.Normal, census.Recall);
            precisionBootCovered += Covers(pilot.Precision.Bootstrap, census.Precision);
            recallBootCovered += Covers(pilot.Recall.Bootstrap, census.Recall);
            reliable += pilot.Precision.IsReliable && pilot.Recall.IsReliable ? 1 : 0;
            precisionError += Math.Abs(pilot.Precision.Estimate - census.Precision);
            recallError += Math.Abs(pilot.Recall.Estimate - census.Recall);
        }

        report.AppendLine(CultureInfo.InvariantCulture, $"| Interval coverage ({CoverageDraws} pilots, seeds 1–{CoverageDraws}) | census inside the 95% interval — precision: normal {precisionCovered}/{CoverageDraws}, bootstrap {precisionBootCovered}/{CoverageDraws} · recall: normal {recallCovered}/{CoverageDraws}, bootstrap {recallBootCovered}/{CoverageDraws}; mean absolute error precision {precisionError / CoverageDraws:0.0000}, recall {recallError / CoverageDraws:0.0000}; estimates without caveats {reliable}/{CoverageDraws} |");
    }

    private static (ReviewSample Sample, ClericalReviewEstimate Precision, ClericalReviewEstimate Recall, int Excluded) RunPilot(
        LinkageAnalysis analysis, IReadOnlyDictionary<string, int> clusterOf, int seed)
    {
        var sample = ClericalReviewSampler.DrawPilot(analysis, PilotPerStratum, seed);
        var ids = clusterOf.Keys.ToList();
        var verdicts = sample.Strata
            .SelectMany(s => s.SelectedRecordIds)
            .SelectMany(record => ids.Where(candidate => candidate != record)
                .Select(candidate => new ReviewVerdict(
                    record,
                    candidate,
                    clusterOf[record] == clusterOf[candidate] ? ReviewOutcome.Same : ReviewOutcome.Different,
                    Reviewer)))
            .ToList();

        var scoring = ClericalReviewScoring.Score(analysis, sample, verdicts, Reviewer);
        return (
            sample,
            ClericalReviewEstimator.Estimate(scoring.Precision, seed),
            ClericalReviewEstimator.Estimate(scoring.Recall, seed),
            scoring.Exclusions.Sum(e => e.Excluded));
    }

    /// <summary>The comparison line (unlabeled estimate vs. labeled pair rates), sensitivity, and the pairwise side-by-side.</summary>
    private static void AppendPairLines(
        StringBuilder report,
        LinkageOptions options,
        LinkageAnalysis analysis,
        IReadOnlyList<RawRecord> records,
        IReadOnlyList<RecordCluster> truth)
    {
        var clusterOf = ClusterIndex(truth);
        long trueMatches = 0, trueNonMatches = 0, falseMatch = 0, falseNonMatch = 0, grayMatch = 0, grayNonMatch = 0;
        foreach (var pair in analysis.PairLinkages)
        {
            var same = clusterOf[pair.RecordIdA] == clusterOf[pair.RecordIdB];
            if (same)
            {
                trueMatches++;
                if (pair.Classification == LinkageClassification.NonMatch) falseNonMatch++;
                if (pair.Classification == LinkageClassification.GrayZone) grayMatch++;
            }
            else
            {
                trueNonMatches++;
                if (pair.Classification == LinkageClassification.Match) falseMatch++;
                if (pair.Classification == LinkageClassification.GrayZone) grayNonMatch++;
            }
        }

        var estimate = analysis.ErrorRates;
        report.AppendLine(CultureInfo.InvariantCulture, $"| Comparison (pairs, {analysis.PairLinkages.Count}) | unlabeled estimate: false-match {Rate(estimate?.FalseMatchRate)} · false-non-match {Rate(estimate?.FalseNonMatchRate)} · gray-zone {Rate(estimate?.GrayZoneShare)} (caveats: {estimate?.Caveats.ToString() ?? "none computed"}) — labeled: false-match {Rate((double)falseMatch / trueNonMatches)} ({falseMatch}/{trueNonMatches}) · false-non-match {Rate((double)falseNonMatch / trueMatches)} ({falseNonMatch}/{trueMatches}) · gray-zone true matches {grayMatch}, true non-matches {grayNonMatch} |");

        var sensitivity = new[] { -2.0, -1.0, 1.0, 2.0 }
            .Select(shift =>
            {
                var shifted = options with { MatchThreshold = options.MatchThreshold + shift, NonMatchThreshold = options.NonMatchThreshold - shift };
                var rates = LinkagePipeline.Analyze(records, shifted).ErrorRates;
                return string.Create(CultureInfo.InvariantCulture, $"match ≥ {shifted.MatchThreshold:0.#} / non-match ≤ {shifted.NonMatchThreshold:0.#}: FM {Rate(rates?.FalseMatchRate)} FNM {Rate(rates?.FalseNonMatchRate)} gray {Rate(rates?.GrayZoneShare)}");
            });
        report.AppendLine(CultureInfo.InvariantCulture, $"| Threshold sensitivity (unlabeled estimate re-fitted) | {string.Join(" · ", sensitivity)} |");

        long predictedPairs = 0, correctPairs = 0;
        foreach (var cluster in analysis.Clustering.Clusters)
        {
            var ids = cluster.RecordIds;
            for (var i = 0; i < ids.Count; i++)
            {
                for (var j = i + 1; j < ids.Count; j++)
                {
                    predictedPairs++;
                    if (clusterOf[ids[i]] == clusterOf[ids[j]]) correctPairs++;
                }
            }
        }

        var pairPrecision = predictedPairs == 0 ? double.NaN : (double)correctPairs / predictedPairs;
        var pairRecall = (double)correctPairs / trueMatches;
        report.AppendLine(CultureInfo.InvariantCulture, $"| Other metrics (census, not the protocol's unit) | pairwise precision {pairPrecision:0.0000} ({correctPairs}/{predictedPairs}) · pairwise recall {pairRecall:0.0000} ({correctPairs}/{trueMatches}) |");
        report.AppendLine("| Out of scope | blocking loss — no blocking step, all pairs compared |");
    }

    private static string Describe(ClericalReviewEstimate estimate) => string.Create(
        CultureInfo.InvariantCulture,
        $"{estimate.Estimate:0.0000} (SE {estimate.StandardError:0.0000}; normal [{estimate.Normal.Low:0.0000}, {estimate.Normal.High:0.0000}] · bootstrap [{estimate.Bootstrap.Low:0.0000}, {estimate.Bootstrap.High:0.0000}]; n {estimate.SampleSize}; caveats {estimate.Caveats})");

    /// <summary>
    /// Inside the interval, give or take rounding: a stratified mean of scores that are all 1.0 sums
    /// its weights to 0.9999999999999999, and a zero-width interval there would otherwise "miss" a
    /// census of exactly 1.
    /// </summary>
    private static int Covers(ScoreInterval interval, double value) =>
        interval.Low - 1e-12 <= value && value <= interval.High + 1e-12 ? 1 : 0;

    private static string Rate(double? value) => value is { } v ? v.ToString("0.00000", CultureInfo.InvariantCulture) : "n/a";

    private static Dictionary<string, int> ClusterIndex(IReadOnlyList<RecordCluster> truth)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < truth.Count; i++)
        {
            foreach (var id in truth[i].RecordIds) index[id] = i;
        }

        return index;
    }

    /// <summary>
    /// <c>rec-12-org</c> and <c>rec-12-dup-0</c> are one entity: the label is the record id up to
    /// its suffix. Values carry a leading space after each comma in the source file; a blank value
    /// is a missing one, which <see cref="FieldComparator"/> does not count as disagreement.
    /// </summary>
    internal static (IReadOnlyList<RawRecord> Records, IReadOnlyList<RecordCluster> Truth) Parse(string csv, int entityModulus)
    {
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var header = lines[0].Split(',').Select(h => h.Trim()).ToArray();
        var records = new List<RawRecord>();
        var entities = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var line in lines.Skip(1))
        {
            var values = line.Split(',').Select(v => v.Trim()).ToArray();
            Assert.Equal(header.Length, values.Length);
            var id = values[0];
            var entity = id[..id.IndexOf('-', 4)];
            if (int.Parse(entity[4..], CultureInfo.InvariantCulture) % entityModulus != 0)
            {
                continue;
            }

            var fields = new Dictionary<string, string?>(StringComparer.Ordinal);
            for (var i = 1; i < header.Length; i++)
            {
                fields[header[i]] = values[i].Length == 0 ? null : values[i];
            }

            records.Add(new RawRecord(id, fields));
            if (!entities.TryGetValue(entity, out var members))
            {
                entities[entity] = members = [];
            }

            members.Add(id);
        }

        return (records, entities.Values.Select(m => new RecordCluster(m)).ToList());
    }

    private static async Task<string> LoadDatasetAsync(Dataset dataset)
    {
        var cacheDirectory = Environment.GetEnvironmentVariable("EYU_BENCHMARK_CACHE") is { Length: > 0 } configured
            ? configured
            : Path.Combine(Path.GetTempPath(), "eyu-benchmarks");
        Directory.CreateDirectory(cacheDirectory);
        var cached = Path.Combine(cacheDirectory, "febrl-" + dataset.File);

        byte[] bytes;
        if (File.Exists(cached))
        {
            bytes = await File.ReadAllBytesAsync(cached, TestContext.Current.CancellationToken);
        }
        else
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            var url = $"https://raw.githubusercontent.com/J535D165/recordlinkage/{MirrorCommit}/recordlinkage/datasets/febrl/{dataset.File}";
            bytes = await http.GetByteArrayAsync(new Uri(url), TestContext.Current.CancellationToken);
        }

        var actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
        Assert.True(actual == dataset.Sha256, $"febrl {dataset.File} hash {actual} does not match the pinned {dataset.Sha256} — refusing to measure on different data.");
        if (!File.Exists(cached))
        {
            await File.WriteAllBytesAsync(cached, bytes, TestContext.Current.CancellationToken);
        }

        return Encoding.UTF8.GetString(bytes);
    }
}
