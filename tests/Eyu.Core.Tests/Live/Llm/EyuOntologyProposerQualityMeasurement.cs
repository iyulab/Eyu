using System.Globalization;
using System.Text;
using Eyu.Core.Grounding;
using Eyu.Core.Judgment;
using Eyu.Core.Proposals;
using Eyu.Core.Records;
using Xunit;
using Xunit.Abstractions;

namespace Eyu.Core.Tests.Live.Llm;

/// <summary>
/// The measurement instrument for <see cref="SinglePassOntologyProposer"/>, mirroring formbase's
/// <c>LlmProposerQualityMeasurement</c> one repo over: runs the proposer repeatedly over a fixed
/// catalog of real <c>league/</c> records from different domains and reports objective quality
/// rates. Eyu's proposal shape is open-vocabulary (entity/relation names the model invents), unlike
/// formbase's fixed column-type schema, so the metrics here differ in kind from formbase's — there
/// is no "expected type" ground truth to score against. What *is* objectively checkable without a
/// human in the loop: strict-parse survival, grounding integrity (every cited source id must be
/// one of the records actually given — <see cref="Eyu.Core.Grounding.GroundedClaim.Create"/> does
/// not itself verify this, so a hallucinated source id would otherwise pass silently), and
/// grounding overlap (<see cref="GroundingOverlapCheck"/> — a valid source id does not by itself
/// mean the claim's content is backed by that record; BD-20260903-02). What this instrument does
/// NOT check: whether records that denote the same real-world entity are actually merged
/// (<see cref="SinglePassOntologyProposer"/>'s prompt carries no resolution instruction — entity
/// resolution accuracy is not measured here). It measures; it does not gate — the
/// packaging/benchmark decision is a human call (see
/// <c>claudedocs/HANDOFF.md</c> "Waiting on you"). Repetitions per case come from
/// <c>EYU_LLM_QUALITY_RUNS</c> (default 2 — lower than formbase's 5: a single real call here
/// observed ~2 minutes, cycle-16); when <c>EYU_LLM_QUALITY_REPORT</c> names a file, the markdown
/// report is also written there.
/// </summary>
public class EyuOntologyProposerQualityMeasurement(ITestOutputHelper output)
{
    private const int DefaultRuns = 2;

    private sealed record QualityCase(string Name, RawRecord[] Records);

    private static readonly QualityCase[] Catalog =
    [
        // league/corpus/nuclear-power/nrc-ler-2020-2026.json -- real NRC Licensee Event Reports.
        new("nuclear-power-ler",
        [
            new("0252022001", new Dictionary<string, string?>
            {
                ["plant_name"] = "Vogtle 3",
                ["event_date"] = "2022-10-06",
                ["title"] = "Automatic Reactor Trip Signal due to Inadequate Procedure Guidance Causing Incorrect Opening of Division B DC Supply Breaker",
            }),
            new("0252022002", new Dictionary<string, string?>
            {
                ["plant_name"] = "Vogtle 3",
                ["event_date"] = "2022-10-23",
                ["title"] = "Automatic Depressurization System Stage 4 Flow Paths Inoperable During Mode 6 with Upper Internals in Place due to Inadequate Work Processes",
            }),
            new("0252022003", new Dictionary<string, string?>
            {
                ["plant_name"] = "Vogtle 3",
                ["event_date"] = "2022-10-24",
                ["title"] = "Unborated Water Flowpath Not Secured per Technical Specification 3.9.2 due to Inadequate Procedure Revision",
            }),
        ]),
        // league/corpus/aviation/SDR-2026.csv -- real FAA Service Difficulty Reports.
        new("aviation-sdr",
        [
            new("AALA202601050865", new Dictionary<string, string?>
            {
                ["aircraft_make"] = "BOEING",
                ["aircraft_model"] = "737823",
                ["part_name"] = "FLOORBEAM",
                ["part_condition"] = "CRACKED",
                ["discrepancy"] = "AIRCRAFT IN BASE MAINTENANCE: CRACK IN PASSENGER CABIN FLOORBEAM AT BS 540, RBL 2. REPLACED BREAK ASSEMBLY PANEL PER AARD 51-00-05-1.",
            }),
            new("AALA202601056048", new Dictionary<string, string?>
            {
                ["aircraft_make"] = "BOEING",
                ["aircraft_model"] = "737823",
                ["part_name"] = "FLOORBEAM",
                ["part_condition"] = "CRACKED",
                ["discrepancy"] = "AIRCRAFT IN BASE MAINTENANCE: CRACK IN PASSENGER CABIN FLOORBEAM AT BS 540, LBL 2. REPLACED BREAK ASSEMBLY PANEL PER AARD 51-00-05-1.",
            }),
            new("CALA2026010212586", new Dictionary<string, string?>
            {
                ["aircraft_make"] = "BOEING",
                ["aircraft_model"] = "767322",
                ["part_name"] = "SEAL",
                ["part_condition"] = "LEAKING",
                ["discrepancy"] = "NUMBER 1 ENG OIL QTY SLOWLY REDUCED TO 0 OVR 90 MINS ALL OTHER ENG IND. NORM AT THIS TIME. CAPT REPORTED THE OIL PRESSURE WAS 92 PSI UPON ENG SHUTDOWN.",
            }),
        ]),
    ];

    private sealed class CaseStats
    {
        public int Attempts;
        public int ParseFailures;
        public int GroundingViolations;
        public int OverlapViolations;
        public readonly List<int> EntityCounts = [];
        public readonly List<int> RelationCounts = [];
        public readonly HashSet<string> EntityTypeShapes = [];
        public readonly List<string> FailureNotes = [];
    }

    [Fact]
    public async Task Measure_proposal_quality_across_the_catalog()
    {
        var runs = int.TryParse(Environment.GetEnvironmentVariable("EYU_LLM_QUALITY_RUNS"), out var parsed) && parsed > 0
            ? parsed
            : DefaultRuns;
        var (httpClient, modelClient) = EyuLlmLiveClient.Create();
        using var _ = httpClient;
        var proposer = new SinglePassOntologyProposer(modelClient);

        var stats = new Dictionary<string, CaseStats>();
        foreach (var qualityCase in Catalog)
        {
            var caseStats = stats[qualityCase.Name] = new CaseStats();
            for (var attempt = 0; attempt < runs; attempt++)
            {
                caseStats.Attempts++;
                await RunAttemptAsync(proposer, qualityCase, caseStats);
            }
        }

        var report = RenderReport(runs, stats);
        output.WriteLine(report);
        var reportPath = Environment.GetEnvironmentVariable("EYU_LLM_QUALITY_REPORT");
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            await File.WriteAllTextAsync(reportPath, report);
        }

        // The instrument's own sanity floor, not a graduation gate: a run where nothing ever
        // parsed measured the transport, not the model.
        Assert.True(stats.Values.Sum(s => s.Attempts - s.ParseFailures) > 0,
            "at least one proposal must survive parsing for the measurement to mean anything");
    }

    private static async Task RunAttemptAsync(SinglePassOntologyProposer proposer, QualityCase qualityCase, CaseStats stats)
    {
        var recordIds = qualityCase.Records.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        OntologyProposal proposal;
        try
        {
            proposal = await proposer.ProposeAsync(declaredStructure: null, qualityCase.Records);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException or ArgumentException)
        {
            stats.ParseFailures++;
            stats.FailureNotes.Add(ex.Message);
            return;
        }

        stats.EntityCounts.Add(proposal.Entities.Count);
        stats.RelationCounts.Add(proposal.Relations.Count);
        stats.EntityTypeShapes.Add(string.Join(",", proposal.Entities.Select(e => e.EntityType).Distinct().OrderBy(t => t, StringComparer.Ordinal)));

        foreach (var entity in proposal.Entities)
        {
            if (entity.Claim.Sources.Any(s => !recordIds.Contains(s.RecordId)))
            {
                stats.GroundingViolations++;
                stats.FailureNotes.Add($"entity {entity.EntityId} cites a source outside the given records");
            }
            else
            {
                // A valid source id (checked above) that shares too little vocabulary with the
                // claim to actually back it — the gap id-validity alone does not catch
                // (BD-20260903-02). Ratio + claim text recorded so a human reviewing the report
                // can tell a genuine mismatch apart from framing language diluting the ratio
                // (heuristic, not semantic — see GroundingOverlapCheck's doc comment).
                var overlap = GroundingOverlapCheck.Evaluate(entity.Claim, qualityCase.Records);
                if (!overlap.IsSupported)
                {
                    stats.OverlapViolations++;
                    stats.FailureNotes.Add(
                        $"entity {entity.EntityId} overlap {overlap.MatchedTokenCount}/{overlap.ClaimTokenCount} ({overlap.Ratio:P0}): \"{entity.Claim.Claim}\"");
                }
            }
        }

        foreach (var relation in proposal.Relations)
        {
            if (relation.Claim.Sources.Any(s => !recordIds.Contains(s.RecordId)))
            {
                stats.GroundingViolations++;
                stats.FailureNotes.Add($"relation {relation.RelationName} cites a source outside the given records");
            }
            else
            {
                var overlap = GroundingOverlapCheck.Evaluate(relation.Claim, qualityCase.Records);
                if (!overlap.IsSupported)
                {
                    stats.OverlapViolations++;
                    stats.FailureNotes.Add(
                        $"relation {relation.RelationName} overlap {overlap.MatchedTokenCount}/{overlap.ClaimTokenCount} ({overlap.Ratio:P0}): \"{relation.Claim.Claim}\"");
                }
            }
        }
    }

    private static string RenderReport(int runs, Dictionary<string, CaseStats> stats)
    {
        var report = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"# SinglePassOntologyProposer quality measurement — {runs} runs/case")
            .AppendLine()
            .AppendLine("| case | parse ok | grounding violations | overlap violations | entities (min-max) | relations (min-max) | distinct type-shapes |")
            .AppendLine("|---|---|---|---|---|---|---|");
        foreach (var (name, s) in stats)
        {
            var parseOk = s.Attempts - s.ParseFailures;
            var entityRange = s.EntityCounts.Count == 0 ? "n/a" : $"{s.EntityCounts.Min()}-{s.EntityCounts.Max()}";
            var relationRange = s.RelationCounts.Count == 0 ? "n/a" : $"{s.RelationCounts.Min()}-{s.RelationCounts.Max()}";
            report.AppendLine(CultureInfo.InvariantCulture,
                $"| {name} | {parseOk}/{s.Attempts} | {s.GroundingViolations} | {s.OverlapViolations} | {entityRange} | {relationRange} | {s.EntityTypeShapes.Count} |");
        }

        var notes = stats.Where(kv => kv.Value.FailureNotes.Count > 0).ToList();
        if (notes.Count > 0)
        {
            report.AppendLine().AppendLine("## Notes");
            foreach (var (name, s) in notes)
            {
                foreach (var note in s.FailureNotes.Distinct())
                {
                    report.AppendLine(CultureInfo.InvariantCulture, $"- {name}: {note}");
                }
            }
        }

        return report.ToString();
    }
}
