using System.Globalization;
using System.Text;
using Eyu.Core.Linkage;
using Eyu.Core.Records;
using Xunit;

namespace Eyu.Core.Tests.Benchmark;

/// <summary>
/// Does the linkage pre-filter reach the same judgment when the same records arrive the way a different
/// source would hand them over? Eyu's port takes a <see cref="RawRecord"/> and nothing about where it came
/// from, so the answer should not depend on the source — but only one source adapter exists, so the
/// differences a second one would bring are modeled here instead: how a missing value is written, how a
/// date or a number is written, whether text is padded or cased, what the fields are called, what the
/// record is identified by. Each variant is a pure rewriting of Febrl's records that keeps what the
/// record says (a missing value stays missing, spelled another way), run through the same pipeline.
/// <para>
/// Two scores per variant, over the clusters the pre-filter assigns: B-cubed F1 against the benchmark's
/// truth, and B-cubed F1 against the clusters the unrewritten records get — «agreement», the number that
/// is 1.000 exactly when the source made no difference. The renaming and the identity rewritings are
/// controls: they cannot change a judgment that reads values, so anything but 1.000 there is a defect of
/// this instrument. «Split» rewrites only the duplicates of each entity, which is two sources describing the
/// same entities. No model is called. Excluded from the default build (<c>-p:IncludeEyuBenchmarks=true</c>);
/// reads the Febrl files <see cref="FebrlLinkageMeasurement"/> reads; <c>EYU_BENCHMARK_REPORT</c> names a
/// file for the markdown report, which carries scores only, never a record.
/// </para>
/// </summary>
public class SourceInvarianceLinkageMeasurement(ITestOutputHelper output)
{
    /// <summary>A rewriting of one record's fields; <c>null</c> is a missing value.</summary>
    private delegate IEnumerable<KeyValuePair<string, string?>> Rewrite(IEnumerable<KeyValuePair<string, string?>> fields);

    private sealed record Variant(string Name, string Axis, bool Control, Rewrite Fields, Func<string, string> Id, Func<string, string?> Key);

    private static string Pad(string value) => value.PadRight(20);

    private static IEnumerable<KeyValuePair<string, string?>> Map(IEnumerable<KeyValuePair<string, string?>> fields, Func<string, string, string?> value) =>
        fields.Select(f => new KeyValuePair<string, string?>(f.Key, f.Value is null ? null : value(f.Key, f.Value)));

    private static IEnumerable<KeyValuePair<string, string?>> MissingAs(IEnumerable<KeyValuePair<string, string?>> fields, string? literal) =>
        fields.Select(f => new KeyValuePair<string, string?>(f.Key, f.Value ?? literal));

    private static bool IsDate(string key, string value) => key == "date_of_birth" && value.Length == 8 && value.All(char.IsAsciiDigit);

    private static readonly Variant[] Variants =
    [
        new("identity", "control", true, f => f, id => id, _ => null),
        new("fields renamed and reordered", "field names", true,
            f => f.Reverse().Select(x => new KeyValuePair<string, string?>("src_" + x.Key.ToUpperInvariant(), x.Value)), id => id, _ => null),
        new("record id rewritten, key and no subject", "identity", true, f => f, id => "x:" + id, id => "K-" + id),

        new("missing as empty string", "missing", false, f => MissingAs(f, ""), id => id, _ => null),
        new("missing as field absent", "missing", false, f => f.Where(x => x.Value is not null), id => id, _ => null),
        new("missing as literal NULL", "missing", false, f => MissingAs(f, "NULL"), id => id, _ => null),
        new("missing as literal N/A", "missing", false, f => MissingAs(f, "N/A"), id => id, _ => null),

        new("date as yyyy-MM-dd", "format", false,
            f => Map(f, (k, v) => IsDate(k, v) ? $"{v[..4]}-{v[4..6]}-{v[6..]}" : v), id => id, _ => null),
        new("date as dd/MM/yyyy", "format", false,
            f => Map(f, (k, v) => IsDate(k, v) ? $"{v[6..]}/{v[4..6]}/{v[..4]}" : v), id => id, _ => null),
        new("numbers as float text (4223.0)", "format", false,
            f => Map(f, (k, v) => k is "postcode" or "street_number" && v.All(char.IsAsciiDigit) ? v + ".0" : v), id => id, _ => null),
        new("postcode zero-padded to 5", "format", false,
            f => Map(f, (k, v) => k == "postcode" && v.All(char.IsAsciiDigit) ? v.PadLeft(5, '0') : v), id => id, _ => null),

        new("every value padded to 20 (CHAR(20))", "text", false, f => Map(f, (_, v) => Pad(v)), id => id, _ => null),
        new("every value upper-cased", "text", false, f => Map(f, (_, v) => v.ToUpperInvariant()), id => id, _ => null),
        new("every value wrapped in spaces", "text", false, f => Map(f, (_, v) => " " + v + " "), id => id, _ => null),
    ];

    private static readonly string[] Identifiers = ["date_of_birth", "soc_sec_id"];

    [Fact]
    public async Task Measure_how_much_the_source_changes_the_linkage_judgment_on_febrl()
    {
        var configurations = new (string Name, LinkageOptions Options, string[] Dropped)[]
        {
            ("exact (default)", LinkageOptions.Default, []),
            ("Jaro-Winkler ≥ 0.90", LinkageOptions.Default with { Comparison = FieldComparison.JaroWinkler() }, []),
            ("exact, without date of birth and identity number", LinkageOptions.Default, Identifiers),
            ("Jaro-Winkler ≥ 0.90, without date of birth and identity number", LinkageOptions.Default with { Comparison = FieldComparison.JaroWinkler() }, Identifiers),
        };

        var report = new StringBuilder();
        report.AppendLine("# Source invariance — does the linkage pre-filter judge the same records the same way when a different source hands them over?");
        report.AppendLine();
        report.AppendLine("Febrl, rewritten as another source would write it; same pipeline, no model. **vs truth** = B-cubed F1 against the benchmark's entities; **agreement** = B-cubed F1 against the clusters the unrewritten records get (1.000 = the source made no difference). Controls must read 1.000 — otherwise the instrument is wrong. *All* rewrites every record; *split* rewrites only each entity's duplicates (two sources describing the same entities).");
        report.AppendLine();

        var controlsBroken = new List<string>();
        foreach (var dataset in FebrlLinkageMeasurement.Datasets)
        {
            var (records, truth) = FebrlLinkageMeasurement.Parse(await FebrlLinkageMeasurement.LoadDatasetAsync(dataset), dataset.EntityModulus);
            report.AppendLine(CultureInfo.InvariantCulture, $"## `{dataset.File}` — {records.Count} records, {truth.Count} entities");
            report.AppendLine();

            foreach (var (config, options, dropped) in configurations)
            {
                var baseRecords = dropped.Length == 0
                    ? records
                    : [.. records.Select(r => new RawRecord(r.Id, r.Fields.Where(f => !dropped.Contains(f.Key)).ToDictionary(f => f.Key, f => f.Value)))];
                var baseline = LinkagePipeline.Analyze(baseRecords, options).Clustering.Clusters;
                var baselineVsTruth = ClusteringMetrics.BCubed(baseline, truth);

                report.AppendLine(CultureInfo.InvariantCulture, $"### {config} — unrewritten: F1 vs truth {baselineVsTruth.F1:0.000}");
                report.AppendLine();
                report.AppendLine("| rewrite | axis | all: vs truth | all: agreement | split: vs truth | split: agreement |");
                report.AppendLine("|---|---|---|---|---|---|");

                foreach (var variant in Variants)
                {
                    var all = Score(baseRecords, variant, options, truth, baseline, splitDuplicatesOnly: false);
                    var split = Score(baseRecords, variant, options, truth, baseline, splitDuplicatesOnly: true);
                    report.AppendLine(CultureInfo.InvariantCulture,
                        $"| {variant.Name} | {variant.Axis}{(variant.Control ? " (control)" : "")} | {all.VsTruth:0.000} | {all.Agreement:0.000} | {split.VsTruth:0.000} | {split.Agreement:0.000} |");
                    if (variant.Control && all.Agreement < 1.0)
                    {
                        controlsBroken.Add($"{dataset.File} / {config} / {variant.Name}: {all.Agreement:0.000}");
                    }
                }

                report.AppendLine();
            }
        }

        output.WriteLine(report.ToString());
        var reportPath = Environment.GetEnvironmentVariable("EYU_BENCHMARK_REPORT");
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            await File.WriteAllTextAsync(reportPath, report.ToString(), TestContext.Current.CancellationToken);
        }

        Assert.True(controlsBroken.Count == 0, "A control rewrite changed the judgment, so this instrument is wrong: " + string.Join("; ", controlsBroken));
    }

    private static (double VsTruth, double Agreement) Score(
        IReadOnlyList<RawRecord> records, Variant variant, LinkageOptions options,
        IReadOnlyList<RecordCluster> truth, IReadOnlyList<RecordCluster> baseline, bool splitDuplicatesOnly)
    {
        var back = new Dictionary<string, string>(StringComparer.Ordinal);
        var rewritten = new List<RawRecord>(records.Count);
        foreach (var record in records)
        {
            var rewrite = !splitDuplicatesOnly || record.Id.Contains("-dup-", StringComparison.Ordinal);
            var id = rewrite ? variant.Id(record.Id) : record.Id;
            back[id] = record.Id;
            var fields = rewrite ? variant.Fields(record.Fields) : record.Fields;
            rewritten.Add(new RawRecord(id, fields.ToDictionary(f => f.Key, f => f.Value), Key: rewrite ? variant.Key(record.Id) : null));
        }

        // The clusters are read back under the original ids so they can be scored against the truth and the baseline.
        var clusters = LinkagePipeline.Analyze(rewritten, options).Clustering.Clusters
            .Select(c => new RecordCluster([.. c.RecordIds.Select(i => back[i])])).ToList();
        return (ClusteringMetrics.BCubed(clusters, truth).F1, ClusteringMetrics.BCubed(clusters, baseline).F1);
    }
}
