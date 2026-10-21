using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Eyu.Core.Judgment;
using Eyu.Core.Linkage;
using Eyu.Core.Proposals;
using Eyu.Core.Tests.Quality;
using Xunit;

namespace Eyu.Core.Tests.Live.Llm;

/// <summary>
/// The LLMs4OL 2026 Reuse task run through <see cref="SinglePassOntologyProposer"/> with type relations
/// asked for: each sample's initial ontology declared (<see cref="Llms4OlReuseCorpus.Declarations"/>), its
/// types in use, its text one document-fragment record, one call per sample. The triples each proposal
/// adds are written beside the sample's gold extension, one line per sample, for both outputs
/// (<see cref="Llms4OlReuseCorpus.Variant"/>); the task's scorer (<c>2026/metrics/graph_similarity.py</c>)
/// scores them, run by the umbrella's <c>scripts/llms4ol-score.py</c> — this class scores nothing.
/// <para>
/// <c>EYU_LLMS4OL_ROOT</c> names the directory holding the challenge checkout under <c>repo/</c> and a
/// <c>SHA256SUMS</c> manifest (paths relative to <c>repo/</c>); <c>EYU_LLMS4OL_OUT</c> the output directory.
/// <c>EYU_LLMS4OL_SPLIT</c> picks <c>test</c> (default) or <c>train</c>, <c>EYU_LLMS4OL_LIMIT</c> the first so
/// many samples in file order (default 10 — a pilot, whose scores are not the task's over its whole split).
/// A call that fails leaves its sample with no prediction — scored as missed — and is counted in the report.
/// </para>
/// </summary>
public class Llms4OlReuseMeasurement(ITestOutputHelper output)
{
    private const int DefaultLimit = 10;

    [Fact]
    public async Task Propose_the_extension_of_each_sample()
    {
        var root = Environment.GetEnvironmentVariable("EYU_LLMS4OL_ROOT");
        var outDirectory = Environment.GetEnvironmentVariable("EYU_LLMS4OL_OUT");
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(outDirectory))
        {
            Assert.Skip("EYU_LLMS4OL_ROOT and EYU_LLMS4OL_OUT are not set — the challenge data lives outside the repository.");
        }

        var split = Environment.GetEnvironmentVariable("EYU_LLMS4OL_SPLIT") is { Length: > 0 } s ? s : "test";
        var limit = int.TryParse(Environment.GetEnvironmentVariable("EYU_LLMS4OL_LIMIT"), out var most) && most > 0 ? most : DefaultLimit;
        var cancellation = TestContext.Current.CancellationToken;

        var relative = $"2026/TaskB-Reuse/{split}_task_b.json";
        var samples = Llms4OlReuseCorpus.Parse(await ReadVerifiedAsync(root, relative, cancellation)).Take(limit).ToList();

        var (httpClient, modelClient) = EyuLlmLiveClient.Create();
        using var _ = httpClient;
        var proposer = new SinglePassOntologyProposer(modelClient, LinkageOptions.Default with { RecordsDenoteEntities = false }, proposeTypeRelations: true);

        var lines = Enum.GetValues<Llms4OlReuseCorpus.Variant>().ToDictionary(v => v, _ => new StringBuilder());
        var failures = new List<string>();
        var (entities, typeRelations, declaredTypeRelations, rejections) = (0, 0, 0, 0);
        var clock = Stopwatch.StartNew();
        foreach (var sample in samples)
        {
            OntologyProposal? proposal = null;
            try
            {
                var declarations = Llms4OlReuseCorpus.Declarations(sample);
                proposal = await proposer.ProposeAsync(declarations, [Llms4OlReuseCorpus.Record(sample)], Llms4OlReuseCorpus.Prior(sample, declarations), cancellation);
                entities += proposal.Entities.Count;
                typeRelations += proposal.TypeRelations.Count;
                declaredTypeRelations += proposal.TypeRelations.Count(t => t.Basis == ProposalBasis.Declared);
                rejections += proposal.Rejections.Count;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellation.IsCancellationRequested)
            {
                failures.Add($"{sample.Id}: {ex.GetType().Name}");
                output.WriteLine($"{sample.Id} failed: {ex.GetType().Name}: {ex.Message}");
            }

            foreach (var (variant, text) in lines)
            {
                text.Append(Llms4OlReuseCorpus.ToJsonLine(sample, proposal is null ? [] : Llms4OlReuseCorpus.Extension(sample, proposal, variant)));
            }
        }

        clock.Stop();
        Directory.CreateDirectory(outDirectory);
        foreach (var (variant, text) in lines)
        {
            await File.WriteAllTextAsync(Path.Combine(outDirectory, $"{variant.ToString().ToLowerInvariant()}.jsonl"), text.ToString(), cancellation);
        }

        var report = new StringBuilder()
            .AppendLine("# LLMs4OL 2026 Reuse — Eyu proposals with type relations")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"Split `{split}`, first {samples.Count} samples. Prompt fingerprints: {SinglePassOntologyProposer.PromptFingerprint} · type relations {SinglePassOntologyProposer.TypeRelationsPromptFingerprint} · known types {SinglePassOntologyProposer.KnownTypesPromptFingerprint}. ")
            .AppendLine("Scores come from the task's scorer over the files written here, not from this table.")
            .AppendLine()
            .AppendLine("| samples | calls failed | entities | type relations (declared) | rejections | gold triples | time |")
            .AppendLine("|---|---|---|---|---|---|---|")
            .AppendLine(CultureInfo.InvariantCulture, $"| {samples.Count} | {failures.Count} | {entities} | {typeRelations} ({declaredTypeRelations}) | {rejections} | {samples.Sum(x => x.Extended.Count)} | {clock.Elapsed.TotalMinutes:0.0} min |");
        foreach (var failure in failures)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"- failed call: {failure}");
        }

        output.WriteLine(report.ToString());
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "report.md"), report.ToString(), cancellation);
    }

    private static async Task<string> ReadVerifiedAsync(string root, string relative, CancellationToken cancellation)
    {
        var manifest = Path.Combine(root, "SHA256SUMS");
        Assert.True(File.Exists(manifest), $"No SHA256SUMS under EYU_LLMS4OL_ROOT ({manifest}).");
        var expected = File.ReadLines(manifest)
            .Where(line => line.Length > 66)
            .Select(line => (Hash: line[..64], Path: line[66..].Trim().TrimStart('*').Replace('\\', '/').TrimStart('.', '/')))
            .FirstOrDefault(e => e.Path == relative).Hash;
        Assert.True(expected is not null, $"{relative} is not in SHA256SUMS.");
        var bytes = await File.ReadAllBytesAsync(Path.Combine(root, "repo", relative), cancellation);
        Assert.True(string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), expected, StringComparison.OrdinalIgnoreCase), $"{relative} does not match SHA256SUMS — the data changed.");
        return Encoding.UTF8.GetString(bytes);
    }
}
