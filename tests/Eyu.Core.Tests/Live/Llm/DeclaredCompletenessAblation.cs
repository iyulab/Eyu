using System.Globalization;
using System.Text;
using System.Text.Json;
using Eyu.Core.Judgment;
using Eyu.Core.Linkage;
using Eyu.Core.Proposals;
using Eyu.Core.Tests.Quality;
using Xunit;

namespace Eyu.Core.Tests.Live.Llm;

/// <summary>
/// Schema-completeness–recall correlation, as an ablation over declared structure. The claim
/// under test is the one form-based methodologies rest on: that a complete declaration of the
/// form guarantees the derivation of its relational elements. Operationalised as in the metadata
/// coverage literature (Liolios et al. 2012 for the coverage index; a Spearman ρ between coverage
/// and extraction recall for the correlation), with the two quantities this harness can actually
/// observe standing in: <b>completeness</b> is the fraction of a case's full form declaration
/// handed to <see cref="SinglePassOntologyProposer"/> as <c>declaredStructure</c>
/// (<see cref="DeclarationLadder"/>, 0 = nothing declared, which is the baseline
/// <see cref="EyuOntologyProposerQualityMeasurement"/> runs), and <b>recall</b> is the share of
/// the case's competency questions the proposal's vocabulary reaches
/// (<see cref="CompetencyQuestionReach"/>). Every rung is run fresh, the same number of times, and
/// each attempt is one point; ρ is reported pooled and per case.
/// <para>
/// What this does and does not test. The reach check is a shallow substring test over type and
/// relation names, so "recall" here means the model kept — or invented — the vocabulary a query
/// would need, not that a query would answer correctly. A declaration can lower reach as well as
/// raise it: <see cref="DeclaredStructureMerge"/> drops a relation proposed under a declared name
/// whose ends contradict the declaration, and the model may name things its own way regardless of
/// what was declared. The declared-basis share (proposals stamped <see cref="ProposalBasis.Declared"/>)
/// is reported beside reach so a fall in reach can be told apart from non-compliance. A case whose
/// questions the baseline already reaches in full has no headroom and can only hold or fall; read
/// the correlation per case before reading it pooled. Two domains of three records each make this
/// a pilot of the operationalisation, not a verdict on the claim over a real corpus. It measures;
/// it does not gate — the only assertion is the instrument's own sanity floor.
/// </para>
/// Reads the same environment as the quality measurement (<c>EYU_LLM_QUALITY_RUNS</c>,
/// <c>EYU_LLM_QUALITY_REPORT</c>, <c>EYU_LLM_QUALITY_STRUCTURED_LOG_DIR</c>) so the same runner
/// script drives both; the structured log file is prefixed <c>ablation-</c> to keep the two
/// run histories apart.
/// </summary>
public class DeclaredCompletenessAblation(ITestOutputHelper output)
{
    private const int DefaultRuns = 2;

    /// <summary>Rungs between nothing and everything: completeness 0, ⅓, ⅔, 1.</summary>
    private const int LadderSteps = 3;

    /// <summary>Fixed so two readings of the same points report the same p-value.</summary>
    private const int PermutationSeed = 20260913;

    /// <summary>The reading thresholds the correlation is held against, as proposed alongside the claim.</summary>
    private const double UpholdThreshold = 0.3;
    private const double WeakenThreshold = 0.1;
    private const double SignificanceLevel = 0.05;

    private static readonly JsonSerializerOptions StructuredLogJsonOptions = new() { WriteIndented = true };

    private sealed class LevelStats
    {
        public int Attempts;
        public int ParseFailures;

        /// <summary>
        /// Attempts lost to a transport/timeout/malformed-response failure rather than to the model's
        /// output failing to parse. Tolerated per-attempt like <see cref="ParseFailures"/> so one
        /// flaky call over a long live round does not discard the whole run's measurement.
        /// </summary>
        public int CallFailures;
        public readonly Dictionary<string, int> QuestionsReached = [];
        public readonly List<double> ReachRates = [];
        public readonly List<double> DeclaredShares = [];

        /// <summary>Distinct structural terms per attempt — how much vocabulary the proposal had at all.</summary>
        public readonly List<int> VocabularySizes = [];

        /// <summary>
        /// Distinct structural terms per attempt that the declaration did not name. What the model
        /// still inferred beyond what it was told; zero at a rung above 0 means declaring switched
        /// inference off, which reach alone cannot show once every named question is reached.
        /// </summary>
        public readonly List<int> BeyondDeclarationCounts = [];

        /// <summary>Reach on the questions the declaration left unnamed, per attempt where any were left.</summary>
        public readonly List<double> InferredReachRates = [];
        public readonly List<string> Notes = [];
    }

    /// <summary>
    /// One parsed attempt. <see cref="InferredReachRate"/> is <c>null</c> at a rung whose declaration
    /// names every question — there is nothing left to infer, and a point with no y is not a point.
    /// </summary>
    private sealed record Point(string Case, double Completeness, double ReachRate, double? InferredReachRate);

    [Fact]
    public async Task Correlate_declared_completeness_with_competency_question_reach()
    {
        var runs = int.TryParse(Environment.GetEnvironmentVariable("EYU_LLM_QUALITY_RUNS"), out var parsed) && parsed > 0
            ? parsed
            : DefaultRuns;
        var (httpClient, modelClient) = EyuLlmLiveClient.Create();
        using var _ = httpClient;
        var proposer = new SinglePassOntologyProposer(modelClient, LinkageOptions.Default);

        // A round long enough to outlast an interactive shell's cut has to run detached, and a
        // detached process has no console — so nothing the test framework writes to stdout survives.
        // The report file is the one artifact such a run can always produce, so any exception that
        // escapes the run loop is persisted there before it fails; otherwise a crash mid-round
        // leaves a non-zero exit code and no reason recorded anywhere.
        var reportPath = Environment.GetEnvironmentVariable("EYU_LLM_QUALITY_REPORT");
        var stats = new Dictionary<string, List<(DeclarationLevel Level, LevelStats Stats)>>();
        var points = new List<Point>();
        try
        {
            foreach (var qualityCase in QualityCatalog.Cases)
            {
                var perLevel = stats[qualityCase.Name] = [];
                foreach (var level in qualityCase.Declaration.Levels(LadderSteps))
                {
                    var levelStats = new LevelStats();
                    perLevel.Add((level, levelStats));
                    for (var attempt = 0; attempt < runs; attempt++)
                    {
                        levelStats.Attempts++;
                        var point = await RunAttemptAsync(proposer, qualityCase, level, levelStats);
                        if (point is not null)
                        {
                            points.Add(point);
                        }
                    }
                }
            }

            var correlations = Correlate(points);
            var report = RenderReport(runs, stats, correlations);
            output.WriteLine(report);

            if (!string.IsNullOrWhiteSpace(reportPath))
            {
                await File.WriteAllTextAsync(reportPath, report, TestContext.Current.CancellationToken);
            }

            var structuredLogDir = Environment.GetEnvironmentVariable("EYU_LLM_QUALITY_STRUCTURED_LOG_DIR");
            if (!string.IsNullOrWhiteSpace(structuredLogDir))
            {
                var timestamp = DateTimeOffset.UtcNow;
                Directory.CreateDirectory(structuredLogDir);
                var logPath = Path.Combine(structuredLogDir, $"ablation-{timestamp:yyyyMMdd-HHmmss}.json");
                await File.WriteAllTextAsync(logPath, BuildStructuredLogJson(timestamp, runs, stats, points, correlations), TestContext.Current.CancellationToken);
            }
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(reportPath))
            {
                // CancellationToken.None: the failure may itself be a cancellation, and the diagnostic
                // must still be written.
                await File.WriteAllTextAsync(
                    reportPath,
                    $"# Declared-completeness ablation FAILED\n\n{points.Count} point(s) collected before the failure.\n\n```\n{ex}\n```\n",
                    System.Threading.CancellationToken.None);
            }
            throw;
        }

        Assert.True(points.Count > 0, "at least one proposal must survive parsing for the ablation to mean anything");
    }

    /// <summary>One attempt at one rung as a point, or <c>null</c> when the response did not parse.</summary>
    private static async Task<Point?> RunAttemptAsync(SinglePassOntologyProposer proposer, QualityCase qualityCase, DeclarationLevel level, LevelStats stats)
    {
        OntologyProposal proposal;
        try
        {
            proposal = await proposer.ProposeAsync(level.Structure, qualityCase.Records);
        }
        catch (FormatException ex)
        {
            stats.ParseFailures++;
            stats.Notes.Add($"completeness {level.Completeness:F2}: {ex.Message}");
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or TimeoutException or IOException or System.Text.Json.JsonException or InvalidOperationException)
        {
            // Transport, per-call timeout (HttpClient.Timeout surfaces as OperationCanceledException),
            // or a malformed response (HttpModelClient throws InvalidOperationException) — none of
            // which is the model's judgment failing to parse. Skip the attempt and carry on, the same
            // way a parse failure does; a systemic outage still ends at zero points and the sanity
            // floor. The proposer is passed no cancellation token, so this cannot swallow test cancel.
            stats.CallFailures++;
            stats.Notes.Add($"completeness {level.Completeness:F2}: call failed — {ex.GetType().Name}: {ex.Message}");
            return null;
        }

        // Left-out elements are part of the result, not a log line — including the relations the
        // declared-structure merge refuses for contradicting a declaration, which this ablation
        // varies directly.
        foreach (var rejection in proposal.Rejections)
        {
            stats.Notes.Add($"completeness {level.Completeness:F2}: rejected {rejection.Reason}: {rejection.Detail}");
        }

        var vocabulary = CompetencyQuestionReach.Vocabulary(proposal);
        var distinctTerms = vocabulary.Select(Normalize).Distinct().ToList();
        var declaredTerms = DeclarationLadder.DeclaredVocabulary(level.Structure).Select(Normalize).ToHashSet();
        stats.VocabularySizes.Add(distinctTerms.Count);
        stats.BeyondDeclarationCounts.Add(distinctTerms.Count(term => !declaredTerms.Contains(term)));

        var reached = 0;
        foreach (var question in qualityCase.Questions)
        {
            stats.QuestionsReached.TryAdd(question.Question, 0);
            if (CompetencyQuestionReach.Reaches(question, vocabulary))
            {
                reached++;
                stats.QuestionsReached[question.Question]++;
            }
            else
            {
                stats.Notes.Add(
                    $"completeness {level.Completeness:F2}: unreachable question \"{question.Question}\" — vocabulary was [{string.Join(", ", vocabulary.Distinct())}]");
            }
        }

        var reachRate = (double)reached / qualityCase.Questions.Length;
        stats.ReachRates.Add(reachRate);

        var inferred = DeclarationLadder.InferredReach(qualityCase.Questions, level.Structure, vocabulary);
        double? inferredRate = inferred.Unnamed == 0 ? null : inferred.Rate;
        if (inferredRate is { } definedInferredRate)
        {
            stats.InferredReachRates.Add(definedInferredRate);
        }

        var proposals = proposal.Entities.Count + proposal.Relations.Count;
        var declared = proposal.Entities.Count(e => e.Basis == ProposalBasis.Declared) + proposal.Relations.Count(r => r.Basis == ProposalBasis.Declared);
        stats.DeclaredShares.Add(proposals == 0 ? 0 : (double)declared / proposals);

        return new Point(qualityCase.Name, level.Completeness, reachRate, inferredRate);
    }

    /// <summary>Same leniency the declared-structure merge uses: case and separators do not make two names different.</summary>
    private static string Normalize(string name) => new([.. name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);

    /// <summary>
    /// Completeness against reach, then completeness against inferred reach — pooled and per case.
    /// The second set drops the rungs where the declaration named every question (no y), so its
    /// n is smaller and it is the correlation the completeness claim would have to survive.
    /// </summary>
    private static Dictionary<string, SpearmanResult?> Correlate(List<Point> points)
    {
        var results = new Dictionary<string, SpearmanResult?> { ["reach · pooled"] = Correlate(points.Select(p => (p.Completeness, p.ReachRate))) };
        foreach (var group in points.GroupBy(p => p.Case))
        {
            results[$"reach · {group.Key}"] = Correlate(group.Select(p => (p.Completeness, p.ReachRate)));
        }

        var inferred = points.Where(p => p.InferredReachRate is not null).ToList();
        results["inferred reach · pooled"] = Correlate(inferred.Select(p => (p.Completeness, p.InferredReachRate!.Value)));
        foreach (var group in inferred.GroupBy(p => p.Case))
        {
            results[$"inferred reach · {group.Key}"] = Correlate(group.Select(p => (p.Completeness, p.InferredReachRate!.Value)));
        }

        return results;

        static SpearmanResult? Correlate(IEnumerable<(double, double)> pairs)
        {
            var list = pairs.ToList();
            return list.Count < 3 ? null : SpearmanCorrelation.Compute(list, PermutationSeed);
        }
    }

    /// <summary>The proposed reading of ρ, with the significance caveat spelled out rather than folded in.</summary>
    private static string Reading(SpearmanResult? result)
    {
        if (result is null)
        {
            return "too few points";
        }

        if (double.IsNaN(result.Rho))
        {
            return "no correlation to read — one variable is constant";
        }

        var band = result.Rho >= UpholdThreshold ? "uphold (ρ ≥ 0.3)"
            : result.Rho >= WeakenThreshold ? "weaken (0.1 ≤ ρ < 0.3)"
            : "withdraw (ρ < 0.1)";
        var significance = result.PValue < SignificanceLevel ? "p < 0.05" : "not significant at 0.05";
        return $"{band}, {significance}";
    }

    private static string RenderReport(int runs, Dictionary<string, List<(DeclarationLevel Level, LevelStats Stats)>> stats, Dictionary<string, SpearmanResult?> correlations)
    {
        var report = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"# Declared-completeness ablation — {runs} runs/rung, {LadderSteps + 1} rungs/case")
            .AppendLine(CultureInfo.InvariantCulture, $"Prompt {SinglePassOntologyProposer.PromptFingerprint} (fixed preamble + declaration clause; changes when that wording changes — runs under different fingerprints are not comparable).")
            .AppendLine()
            .AppendLine("Completeness = share of the case's full form declaration passed as declared structure (0 = nothing declared, the baseline). ")
            .AppendLine("Reach = share of the case's competency questions the proposal's vocabulary reaches (substring over type and relation names — presence, not correctness). ")
            .AppendLine("Named by declaration = questions the declaration's own vocabulary (subject, relation names, targets) already reaches; the ceiling a compliant model would hit. ")
            .AppendLine("Declared share = proposals stamped Declared by the merge; low at high completeness means the model named things its own way or the merge dropped contradicting relations. ")
            .AppendLine("Vocabulary = mean distinct structural terms per attempt; beyond declaration = mean of those the declaration did not name — zero above completeness 0 means declaring switched inference off. ")
            .AppendLine("Inferred reach = reach on the questions the declaration did not name (n/a where it named all of them) — the part of reach a declaration cannot account for.")
            .AppendLine()
            .AppendLine("| case | completeness | items | named by declaration | usable | mean reach | inferred reach | declared share | vocabulary | beyond declaration | per question |")
            .AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var (name, levels) in stats)
        {
            var questions = QualityCatalog.Cases.Single(c => c.Name == name).Questions;
            foreach (var (level, s) in levels)
            {
                var named = questions.Count(q => CompetencyQuestionReach.Reaches(q, DeclarationLadder.DeclaredVocabulary(level.Structure)));
                var usable = s.Attempts - s.ParseFailures - s.CallFailures;
                var meanReach = s.ReachRates.Count == 0 ? "n/a" : s.ReachRates.Average().ToString("F2", CultureInfo.InvariantCulture);
                var declaredShare = s.DeclaredShares.Count == 0 ? "n/a" : s.DeclaredShares.Average().ToString("F2", CultureInfo.InvariantCulture);
                var inferredReach = s.InferredReachRates.Count == 0 ? "n/a" : s.InferredReachRates.Average().ToString("F2", CultureInfo.InvariantCulture);
                var vocabularySize = s.VocabularySizes.Count == 0 ? "n/a" : s.VocabularySizes.Average().ToString("F1", CultureInfo.InvariantCulture);
                var beyond = s.BeyondDeclarationCounts.Count == 0 ? "n/a" : s.BeyondDeclarationCounts.Average().ToString("F1", CultureInfo.InvariantCulture);
                var perQuestion = string.Join(" · ", questions.Select(q => $"{s.QuestionsReached.GetValueOrDefault(q.Question, 0)}/{usable}"));
                report.AppendLine(CultureInfo.InvariantCulture,
                    $"| {name} | {level.Completeness:F2} | {level.ItemsKept}/{level.ItemsTotal} | {named}/{questions.Length} | {usable}/{s.Attempts} | {meanReach} | {inferredReach} | {declaredShare} | {vocabularySize} | {beyond} | {perQuestion} |");
            }
        }

        report.AppendLine().AppendLine("## Spearman ρ, completeness vs. reach and vs. inferred reach (one point per parsed attempt; permutation p, two-sided)")
            .AppendLine()
            .AppendLine("| scope | n | ρ | p | reading |")
            .AppendLine("|---|---|---|---|---|");
        foreach (var (scope, result) in correlations)
        {
            var rho = result is null || double.IsNaN(result.Rho) ? "n/a" : result.Rho.ToString("F3", CultureInfo.InvariantCulture);
            var p = result is null || double.IsNaN(result.PValue) ? "n/a" : result.PValue.ToString("F4", CultureInfo.InvariantCulture);
            report.AppendLine(CultureInfo.InvariantCulture, $"| {scope} | {result?.N.ToString(CultureInfo.InvariantCulture) ?? "-"} | {rho} | {p} | {Reading(result)} |");
        }

        report.AppendLine()
            .AppendLine("Reading guide: uphold / weaken / withdraw are the proposed bands for the completeness claim; a case already at full reach with nothing declared has no headroom and tests only whether declaring can harm. Read per case before pooled. ")
            .AppendLine("Plain reach rises with completeness whenever the model complies with what was declared, so read the claim off inferred reach — the questions the declaration did not hand the model — and treat the plain-reach row as the compliance check.");

        var notes = stats.Where(kv => kv.Value.Any(l => l.Stats.Notes.Count > 0)).ToList();
        if (notes.Count > 0)
        {
            report.AppendLine().AppendLine("## Notes");
            foreach (var (name, levels) in notes)
            {
                foreach (var note in levels.SelectMany(l => l.Stats.Notes).Distinct())
                {
                    report.AppendLine(CultureInfo.InvariantCulture, $"- {name}: {note}");
                }
            }
        }

        return report.ToString();
    }

    private sealed record LevelLogEntry(
        double Completeness,
        int ItemsKept,
        int ItemsTotal,
        int NamedByDeclaration,
        int Attempts,
        int ParseFailures,
        int CallFailures,
        double? MeanReach,
        double? MeanInferredReach,
        double? MeanDeclaredShare,
        double? MeanVocabularySize,
        double? MeanBeyondDeclaration,
        IReadOnlyDictionary<string, int> QuestionsReached);

    private sealed record CaseLogEntry(string Name, IReadOnlyList<LevelLogEntry> Levels);

    private sealed record PointLogEntry(string Case, double Completeness, double ReachRate, double? InferredReachRate);

    private sealed record CorrelationLogEntry(string Scope, int? N, double? Rho, double? PValue, string Reading);

    private sealed record StructuredLogEntry(
        string Timestamp,
        int Runs,
        int LadderSteps,
        IReadOnlyList<CaseLogEntry> Cases,
        IReadOnlyList<PointLogEntry> Points,
        IReadOnlyList<CorrelationLogEntry> Correlations);

    private static string BuildStructuredLogJson(
        DateTimeOffset timestamp,
        int runs,
        Dictionary<string, List<(DeclarationLevel Level, LevelStats Stats)>> stats,
        List<Point> points,
        Dictionary<string, SpearmanResult?> correlations)
    {
        var cases = stats.Select(kv =>
        {
            var questions = QualityCatalog.Cases.Single(c => c.Name == kv.Key).Questions;
            return new CaseLogEntry(kv.Key,
            [
                .. kv.Value.Select(l => new LevelLogEntry(
                    l.Level.Completeness,
                    l.Level.ItemsKept,
                    l.Level.ItemsTotal,
                    questions.Count(q => CompetencyQuestionReach.Reaches(q, DeclarationLadder.DeclaredVocabulary(l.Level.Structure))),
                    l.Stats.Attempts,
                    l.Stats.ParseFailures,
                    l.Stats.CallFailures,
                    l.Stats.ReachRates.Count == 0 ? null : l.Stats.ReachRates.Average(),
                    l.Stats.InferredReachRates.Count == 0 ? null : l.Stats.InferredReachRates.Average(),
                    l.Stats.DeclaredShares.Count == 0 ? null : l.Stats.DeclaredShares.Average(),
                    l.Stats.VocabularySizes.Count == 0 ? null : l.Stats.VocabularySizes.Average(),
                    l.Stats.BeyondDeclarationCounts.Count == 0 ? null : l.Stats.BeyondDeclarationCounts.Average(),
                    l.Stats.QuestionsReached)),
            ]);
        }).ToList();

        var entry = new StructuredLogEntry(
            timestamp.ToString("O", CultureInfo.InvariantCulture),
            runs,
            LadderSteps,
            cases,
            [.. points.Select(p => new PointLogEntry(p.Case, p.Completeness, p.ReachRate, p.InferredReachRate))],
            [.. correlations.Select(kv => new CorrelationLogEntry(
                kv.Key,
                kv.Value?.N,
                kv.Value is null || double.IsNaN(kv.Value.Rho) ? null : kv.Value.Rho,
                kv.Value is null || double.IsNaN(kv.Value.PValue) ? null : kv.Value.PValue,
                Reading(kv.Value)))]);

        return JsonSerializer.Serialize(entry, StructuredLogJsonOptions);
    }
}
