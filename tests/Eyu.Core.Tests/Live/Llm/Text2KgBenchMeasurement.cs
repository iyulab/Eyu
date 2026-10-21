using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Eyu.Core.Judgment;
using Eyu.Core.Linkage;
using Eyu.Core.Proposals;
using Eyu.Core.Records;
using Eyu.Core.Tests.Quality;
using Xunit;

namespace Eyu.Core.Tests.Live.Llm;

/// <summary>
/// Text2KGBench (Mihindukulasooriya et al., ISWC 2023) run through <see cref="SinglePassOntologyProposer"/>:
/// each ontology declared (<see cref="Text2KgBenchCorpus.Declarations"/>), its human-verified sentences
/// proposed in batches as document-fragment records, and the relations written back as the benchmark's
/// system-output files — everything the proposals hold, and only what they base on the declaration. The
/// benchmark's own scorer (<c>src/evaluation/run_eval.py</c>) scores them; this class writes its inputs
/// and a configuration file per output pointing at them, and scores nothing itself, so the numbers stand
/// beside the baselines the benchmark ships.
/// <para>
/// <c>EYU_TEXT2KGBENCH_ROOT</c> names the directory holding the benchmark checkout under <c>repo/</c> and a
/// <c>SHA256SUMS</c> manifest (paths relative to <c>repo/</c>) every file read is checked against;
/// <c>EYU_TEXT2KGBENCH_OUT</c> the directory the outputs go to. <c>EYU_TEXT2KGBENCH_ONTOLOGIES</c> picks
/// Wikidata-TekGen ontologies (default <c>1_movie,3_sport</c>), <c>EYU_TEXT2KGBENCH_BATCH</c> the sentences
/// per call (default 10), <c>EYU_TEXT2KGBENCH_LIMIT</c> the most sentences per ontology (default all — a
/// smaller number is a smoke run whose scores are not the benchmark's). A call that fails leaves its sentences with no triples — scored as missed — and
/// is counted in the report. <c>EYU_TEXT2KGBENCH_BATCHES</c> — the first sentence id of each batch to run, comma-separated — runs only those
/// batches (the ones a round's failed calls name), batched as the full round batches them, and writes only their sentences, for merging back.
/// </para>
/// </summary>
public class Text2KgBenchMeasurement(ITestOutputHelper output)
{
    private const string Dataset = "wikidata_tekgen";
    private const int DefaultBatch = 10;

    private static readonly string[] DefaultOntologies = ["1_movie", "3_sport"];

    [Fact]
    public async Task Propose_triples_for_the_verified_sentences()
    {
        var root = Environment.GetEnvironmentVariable("EYU_TEXT2KGBENCH_ROOT");
        var outDirectory = Environment.GetEnvironmentVariable("EYU_TEXT2KGBENCH_OUT");
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(outDirectory))
        {
            Assert.Skip("EYU_TEXT2KGBENCH_ROOT and EYU_TEXT2KGBENCH_OUT are not set — the benchmark data lives outside the repository.");
        }

        var ontologies = Environment.GetEnvironmentVariable("EYU_TEXT2KGBENCH_ONTOLOGIES") is { Length: > 0 } names
            ? names.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            : DefaultOntologies;
        var batchSize = int.TryParse(Environment.GetEnvironmentVariable("EYU_TEXT2KGBENCH_BATCH"), out var size) && size > 0 ? size : DefaultBatch;
        var limit = int.TryParse(Environment.GetEnvironmentVariable("EYU_TEXT2KGBENCH_LIMIT"), out var most) && most > 0 ? most : int.MaxValue;

        var repo = Path.Combine(root, "repo");
        var manifest = ReadManifest(root);
        var cancellation = TestContext.Current.CancellationToken;

        var (httpClient, modelClient) = EyuLlmLiveClient.Create();
        using var _ = httpClient;
        var proposer = new SinglePassOntologyProposer(modelClient, LinkageOptions.Default with { RecordsDenoteEntities = false });

        foreach (var variant in Variants)
        {
            Directory.CreateDirectory(Path.Combine(outDirectory, variant.Name));
        }

        var report = new StringBuilder()
            .AppendLine("# Text2KGBench — Eyu proposals (Wikidata-TekGen, human-verified sentences)")
            .AppendLine()
            .AppendLine(string.Create(CultureInfo.InvariantCulture, $"Sentences per call: {batchSize}. Records are document fragments (`RecordsDenoteEntities = false`). ")
                + "Scores come from the benchmark's scorer over the files written here, not from this table.")
            .AppendLine()
            .AppendLine("| ontology | concepts | relations declared (undeclarable) | sentences | calls (failed) | relations proposed (declared basis) | sentences with a triple: all · declared | time |")
            .AppendLine("|---|---|---|---|---|---|---|---|");

        foreach (var ontology in ontologies)
        {
            var ontologyJson = JsonDocument.Parse(await ReadVerifiedAsync(manifest, repo, $"data/{Dataset}/ontologies/{ontology}_ontology.json", cancellation)).RootElement;
            var (declarations, undeclarable) = Text2KgBenchCorpus.Declarations(ontologyJson);

            var selected = (await ReadVerifiedAsync(manifest, repo, $"data/{Dataset}/manually_verified_sentences/selected_ont_{ontology}.txt", cancellation))
                .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Take(limit)
                .ToArray();
            var sentences = (await ReadVerifiedAsync(manifest, repo, $"data/{Dataset}/test/ont_{ontology}_test.jsonl", cancellation))
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => JsonDocument.Parse(line).RootElement)
                .ToDictionary(e => e.GetProperty("id").GetString()!, e => e.GetProperty("sent").GetString()!, StringComparer.Ordinal);
            var missing = selected.Where(id => !sentences.ContainsKey(id)).ToList();
            Assert.True(missing.Count == 0, $"{ontology}: verified ids not in the test split: {string.Join(", ", missing.Take(5))}");

            var only = Environment.GetEnvironmentVariable("EYU_TEXT2KGBENCH_BATCHES") is { Length: > 0 } firsts
                ? firsts.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal)
                : null;
            var batches = selected.Chunk(batchSize).Where(b => only is null || only.Contains(b[0])).ToList();
            if (only is not null)
            {
                selected = [.. batches.SelectMany(b => b)];
            }

            var clock = Stopwatch.StartNew();
            var proposals = new List<OntologyProposal>();
            var calls = 0;
            var failures = new List<string>();
            foreach (var batch in batches)
            {
                calls++;
                var records = batch.Select(id => Text2KgBenchCorpus.Sentence(id, sentences[id])).ToList();
                try
                {
                    proposals.Add(await proposer.ProposeAsync(declarations, records, cancellation));
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellation.IsCancellationRequested)
                {
                    failures.Add($"{batch[0]}: {ex.GetType().Name}");
                    output.WriteLine($"{ontology} call {calls} ({batch[0]}…) failed: {ex.GetType().Name}: {ex.Message}");
                }
            }

            clock.Stop();

            var counts = new List<int>();
            foreach (var variant in Variants)
            {
                var triples = Text2KgBenchCorpus.Triples(proposals, selected, variant.DeclaredOnly);
                counts.Add(triples.Count(t => t.Value.Count > 0));
                await File.WriteAllTextAsync(
                    Path.Combine(outDirectory, variant.Name, $"ont_{ontology}_responses.jsonl"),
                    Text2KgBenchCorpus.ToJsonLines(selected, triples),
                    cancellation);
            }

            var relations = proposals.Sum(p => p.Relations.Count);
            var declaredRelations = proposals.Sum(p => p.Relations.Count(r => r.Basis == ProposalBasis.Declared));
            report.AppendLine(CultureInfo.InvariantCulture,
                $"| {ontology} | {declarations.Count} | {declarations.Sum(d => d.Relations.Count)} ({undeclarable.Count}) | {selected.Length} | " +
                $"{calls} ({failures.Count}) | {relations} ({declaredRelations}) | {counts[0]} · {counts[1]} | {clock.Elapsed.TotalMinutes:0.0} min |");
            foreach (var failure in failures)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"- {ontology} failed call: {failure}");
            }
        }

        foreach (var variant in Variants)
        {
            await File.WriteAllTextAsync(
                Path.Combine(outDirectory, variant.Name, "eval_config.json"),
                EvalConfig(repo, Path.Combine(outDirectory, variant.Name), ontologies),
                cancellation);
        }

        output.WriteLine(report.ToString());
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "report.md"), report.ToString(), cancellation);
    }

    private static readonly (string Name, bool DeclaredOnly)[] Variants = [("all", false), ("declared", true)];

    /// <summary>The scorer's configuration: the benchmark's ground truth and ontologies, this run's outputs.</summary>
    private static string EvalConfig(string repo, string directory, IReadOnlyList<string> ontologies) =>
        JsonSerializer.Serialize(
            new Dictionary<string, object>
            {
                ["onto_list"] = ontologies,
                ["path_patterns"] = new Dictionary<string, string>
                {
                    ["sys"] = Path.Combine(directory, "ont_$$onto$$_responses.jsonl"),
                    ["gt"] = Path.Combine(repo, "data", Dataset, "ground_truth", "ont_$$onto$$_ground_truth.jsonl"),
                    ["selected_ids"] = Path.Combine(repo, "data", Dataset, "manually_verified_sentences", "selected_ont_$$onto$$.txt"),
                    ["onto"] = Path.Combine(repo, "data", Dataset, "ontologies", "$$onto$$_ontology.json"),
                    ["output"] = Path.Combine(directory, "ont_$$onto$$_stats.jsonl"),
                },
                ["avg_out_file"] = Path.Combine(directory, "avg_stats.jsonl"),
            },
            IndentedJson);

    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    /// <summary>The manifest's sha256 per path relative to <c>repo/</c>.</summary>
    private static Dictionary<string, string> ReadManifest(string root)
    {
        var path = Path.Combine(root, "SHA256SUMS");
        Assert.True(File.Exists(path), $"No SHA256SUMS under EYU_TEXT2KGBENCH_ROOT ({path}).");
        return File.ReadLines(path)
            .Where(line => line.Length > 66)
            .ToDictionary(line => line[66..].Trim().TrimStart('*').Replace('\\', '/').TrimStart('.', '/'), line => line[..64], StringComparer.Ordinal);
    }

    private static async Task<string> ReadVerifiedAsync(Dictionary<string, string> manifest, string repo, string relative, CancellationToken cancellation)
    {
        Assert.True(manifest.TryGetValue(relative, out var expected), $"{relative} is not in SHA256SUMS.");
        var bytes = await File.ReadAllBytesAsync(Path.Combine(repo, relative), cancellation);
        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Assert.True(actual == expected, $"{relative} does not match SHA256SUMS — the data changed.");
        return Encoding.UTF8.GetString(bytes);
    }
}
