using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Eyu.Core.Linkage;
using Eyu.Core.Records;
using Eyu.Core.Tests.Quality;
using Xunit;

namespace Eyu.Core.Tests.Benchmark;

/// <summary>
/// Pair-level accuracy of the linkage pre-filter on the entity-matching benchmarks of the DeepMatcher
/// study (Mudgal et al., SIGMOD 2018): two tables of one schema, each free of duplicates, and labeled
/// candidate pairs split into train, validation and test. Scored as the field scores those benchmarks —
/// pair-level F1 on the test split — with the pre-filter as Eyu runs it: no labels are used to fit it.
/// EM fits the match and non-match agreement probabilities on the comparison vectors of every candidate
/// pair (train, validation and test, labels ignored), the default thresholds classify, and the labels are
/// read only to score the test pairs. The candidate pairs are the benchmark's own (it ships them blocked),
/// so the batch-wide pair generation of <see cref="LinkagePipeline"/> is not what is measured here; the
/// comparator, the estimator and the classifier are.
/// <para>
/// The data is not in this repository and has no mirror to fetch from: <c>EYU_DEEPMATCHER_ROOT</c> names a
/// directory holding the benchmarks' extracted files under <c>exp/&lt;Type&gt;/&lt;Dataset&gt;/exp_data/</c>
/// and a <c>SHA256SUMS</c> manifest, which every file read is checked against. Without the variable the
/// measurement is skipped. Excluded from the default build (<c>-p:IncludeEyuBenchmarks=true</c>);
/// <c>EYU_BENCHMARK_REPORT</c> names a file for the markdown report. The report carries counts and
/// scores only, never a record.
/// </para>
/// </summary>
public class DeepMatcherLinkageMeasurement(ITestOutputHelper output)
{
    private static readonly string[] Datasets =
    [
        "Structured/Beer", "Structured/iTunes-Amazon", "Structured/Fodors-Zagats", "Structured/DBLP-ACM",
        "Structured/DBLP-GoogleScholar", "Structured/Amazon-Google", "Structured/Walmart-Amazon",
        "Dirty/iTunes-Amazon", "Dirty/DBLP-ACM", "Dirty/DBLP-GoogleScholar", "Dirty/Walmart-Amazon",
        "Textual/Abt-Buy", "Textual/Company",
    ];

    private static readonly string[] Splits = ["train", "valid", "test"];

    /// <summary>Random pairs drawn to measure <c>u</c>; the library's default budget is larger than these tables need.</summary>
    private const int NonMatchSamplePairs = 20_000;

    [Fact]
    public async Task Measure_pair_level_accuracy_on_deepmatcher_benchmarks()
    {
        var root = Environment.GetEnvironmentVariable("EYU_DEEPMATCHER_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            Assert.Skip("EYU_DEEPMATCHER_ROOT is not set — the benchmark data lives outside the repository.");
        }

        var manifest = ReadManifest(root);
        var selected = Environment.GetEnvironmentVariable("EYU_DEEPMATCHER_DATASETS") is { Length: > 0 } names
            ? [.. names.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)]
            : Datasets;

        // «u from random pairs»: each field's non-match agreement measured over random pairs of the two
        // tables pooled (nearly all non-matches — each table is free of duplicates) and held while EM fits
        // m and the prior — the fit the library documents for candidates that gather duplicates.
        var similarity = LinkageOptions.Default with { Comparison = FieldComparison.JaroWinkler };
        var configurations = new (string Name, LinkageOptions Options, bool FixU)[]
        {
            ("exact", LinkageOptions.Default, false),
            ("Jaro-Winkler ≥ 0.90", similarity, false),
            ("exact, u from random pairs", LinkageOptions.Default, true),
            ("Jaro-Winkler ≥ 0.90, u from random pairs", similarity, true),
        };

        var report = new StringBuilder()
            .AppendLine("# Entity matching — DeepMatcher benchmarks (pair-level, test split)")
            .AppendLine()
            .AppendLine("Unsupervised: EM fits on every candidate pair's comparison vector (train, validation and test, labels ignored); default thresholds " +
                $"(match ≥ {LinkageClassifier.DefaultMatchThreshold:0}, non-match ≤ {LinkageClassifier.DefaultNonMatchThreshold:0}) classify; labels score the test pairs only. " +
                "«alone» counts a gray-zone pair as no match; «gray right» is the same with every gray-zone pair adjudicated correctly — the ceiling of a model step; " +
                "«oracle» is the best single threshold chosen on the test labels — what the ratios separate, not a result. Fields: every column but `id`.")
            .AppendLine()
            .AppendLine("| dataset | config | test pairs (matches) | EM | match prior | alone P / R / F1 | gray share | gray right F1 | oracle F1 (threshold) | time |")
            .AppendLine("|---|---|---|---|---|---|---|---|---|---|");

        foreach (var dataset in selected)
        {
            var directory = Path.Combine(root, "exp", dataset.Replace('/', Path.DirectorySeparatorChar), "exp_data");
            var left = Table(await ReadVerifiedAsync(manifest, root, directory, "tableA.csv"), "A");
            var right = Table(await ReadVerifiedAsync(manifest, root, directory, "tableB.csv"), "B");
            var pairs = new List<(string Split, RawRecord A, RawRecord B, bool Match)>();
            foreach (var split in Splits)
            {
                foreach (var row in Rfc4180.ReadRecords(await ReadVerifiedAsync(manifest, root, directory, $"{split}.csv")))
                {
                    pairs.Add((split, left[row["ltable_id"]], right[row["rtable_id"]], row["label"] == "1"));
                }
            }

            foreach (var (name, options, fixU) in configurations)
            {
                var clock = Stopwatch.StartNew();
                var vectors = pairs.Select(p => FieldComparator.Compare(p.A, p.B, options)).ToList();
                var u = fixU ? LinkagePipeline.EstimateNonMatchAgreement([.. left.Values, .. right.Values], options, NonMatchSamplePairs) : null;
                var parameters = FellegiSunterEstimator.Estimate(vectors, options.MaxIterations, options.ConvergenceTolerance, u);
                var test = pairs.Select((p, i) => (p, i))
                    .Where(x => x.p.Split == "test")
                    .Select(x =>
                    {
                        var ratio = FellegiSunterEstimator.ComputeLogLikelihoodRatio(vectors[x.i], parameters);
                        return new ScoredPair(LinkageClassifier.Classify(ratio, options.MatchThreshold, options.NonMatchThreshold), ratio, x.p.Match);
                    })
                    .ToList();
                clock.Stop();

                var alone = CandidatePairScoring.PreFilterAlone(test);
                var grayRight = CandidatePairScoring.GrayAdjudicatedCorrectly(test);
                var (oracleF1, oracleThreshold) = CandidatePairScoring.OracleThreshold(test);
                report.AppendLine(CultureInfo.InvariantCulture,
                    $"| {dataset} | {name} | {test.Count} ({test.Count(p => p.IsMatch)}) | {parameters.Status} | {parameters.MatchPrior:0.000} | " +
                    $"{alone.Precision:0.000} / {alone.Recall:0.000} / **{alone.F1:0.000}** | {CandidatePairScoring.GrayShare(test):0.000} | " +
                    $"{grayRight.F1:0.000} | {oracleF1:0.000} ({oracleThreshold:0.0}) | {clock.Elapsed.TotalSeconds:0.0} s |");
            }
        }

        output.WriteLine(report.ToString());
        var reportPath = Environment.GetEnvironmentVariable("EYU_BENCHMARK_REPORT");
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            await File.WriteAllTextAsync(reportPath, report.ToString(), TestContext.Current.CancellationToken);
        }
    }

    /// <summary>A table's rows as records under <c>{side}:{id}</c>, every column but <c>id</c> a field.</summary>
    private static Dictionary<string, RawRecord> Table(string text, string side) =>
        Rfc4180.ReadRecords(text).ToDictionary(
            row => row["id"],
            row => new RawRecord(
                $"{side}:{row["id"]}",
                row.Where(f => f.Key != "id").ToDictionary(f => f.Key, f => (string?)f.Value, StringComparer.Ordinal)),
            StringComparer.Ordinal);

    /// <summary>The manifest's sha256 per path relative to the root.</summary>
    private static Dictionary<string, string> ReadManifest(string root)
    {
        var path = Path.Combine(root, "SHA256SUMS");
        Assert.True(File.Exists(path), $"No SHA256SUMS under EYU_DEEPMATCHER_ROOT ({path}).");
        return File.ReadLines(path)
            .Where(line => line.Length > 66)
            .ToDictionary(line => line[66..].Trim().Replace('\\', '/'), line => line[..64], StringComparer.Ordinal);
    }

    private static async Task<string> ReadVerifiedAsync(Dictionary<string, string> manifest, string root, string directory, string file)
    {
        var path = Path.Combine(directory, file);
        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        Assert.True(manifest.TryGetValue(relative, out var expected), $"{relative} is not in SHA256SUMS.");
        var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Assert.True(actual == expected, $"{relative} does not match SHA256SUMS — the data changed.");
        return Encoding.UTF8.GetString(bytes);
    }
}
