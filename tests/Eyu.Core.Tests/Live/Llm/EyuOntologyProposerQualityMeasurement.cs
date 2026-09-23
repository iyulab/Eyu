using System.Globalization;
using System.Text;
using System.Text.Json;
using Eyu.Core.Declared;
using Eyu.Core.Grounding;
using Eyu.Core.Judgment;
using Eyu.Core.Linkage;
using Eyu.Core.Proposals;
using Eyu.Core.Records;
using Eyu.Core.Tests.Quality;
using Eyu.Rdf;
using Xunit;

namespace Eyu.Core.Tests.Live.Llm;

/// <summary>
/// The measurement instrument for <see cref="SinglePassOntologyProposer"/>, mirroring formbase's
/// <c>LlmProposerQualityMeasurement</c>: runs the proposer repeatedly over a fixed catalog of real
/// public-dataset records from different domains and reports objective quality
/// rates. Eyu's proposal shape is open-vocabulary (entity/relation names the model invents), unlike
/// formbase's fixed column-type schema, so the metrics here differ in kind from formbase's — there
/// is no "expected type" ground truth to score against. What *is* objectively checkable without a
/// human in the loop: strict-parse survival, grounding integrity (every cited source id must be
/// one of the records actually given — <see cref="Eyu.Core.Grounding.GroundedClaim.Create"/> does
/// not itself verify this, so a hallucinated source id would otherwise pass silently), and
/// grounding overlap (<see cref="GroundingOverlapCheck"/> — a valid source id does not by itself
/// mean the claim's content is backed by that record). What this instrument does
/// NOT check: whether records that denote the same real-world entity are actually merged
/// (<see cref="SinglePassOntologyProposer"/>'s prompt carries no resolution instruction — entity
/// resolution accuracy is not measured here). It measures; it does not gate — the
/// packaging/benchmark decision is a human call. Comparing one run against another used to mean
/// re-reading prose reports by hand; when
/// <c>EYU_LLM_QUALITY_STRUCTURED_LOG_DIR</c> names a directory, this run's stats are additionally
/// written there as one timestamped JSON file — a git-friendly run-history directory (borrowing
/// only <c>mloop</c>'s filesystem/git-based run-record convention, not a dependency on it: `mloop`
/// itself targets ML.NET AutoML training runs, not LLM proposal quality). Unset by default, so a
/// plain `dotnet test` run never writes one. <c>EYU_LLM_QUALITY_CASES</c>, when set, limits the
/// run to the named catalog cases (comma-separated). Repetitions per case come from
/// <c>EYU_LLM_QUALITY_RUNS</c> (default 2 — lower than formbase's 5: a single real call here was
/// observed to take ~2 minutes); when <c>EYU_LLM_QUALITY_REPORT</c> names a file, the markdown
/// report is also written there. The Fellegi-Sunter pre-filter's <see cref="LinkageOptions"/> is
/// also environment-tunable (<c>EYU_LLM_QUALITY_MATCH_THRESHOLD</c>/
/// <c>_NONMATCH_THRESHOLD</c>/<c>_MAX_ITERATIONS</c>/<c>_CONVERGENCE_TOLERANCE</c>, each falling
/// back to <see cref="LinkageOptions.Default"/> when unset) — this is the harness design decision
/// 4 anticipated ("관측된 precision/recall로 임계값 튜닝"): a live validation cycle re-runs this
/// measurement with different thresholds to see the effect, rather than measuring a fixed
/// heuristic. The chosen options and, per case, the resulting <see cref="LinkageAnalysis"/>
/// (pair classifications, EM <see cref="EstimationStatus"/>, match prior) are recorded in the
/// report — <see cref="LinkagePipeline.Analyze"/> depends only on records and options, not the
/// model, so it is computed once per case rather than once per attempt.
/// <para>
/// A fourth axis (pilot): <b>competency-question answerability</b>. Each catalog case carries a
/// short list of questions the proposed structure is supposed to be able to answer, and the run
/// reports how many of them the proposal's vocabulary can reach. The check is deliberately
/// shallow — a question counts as reachable when every concept it names appears among the
/// proposed entity types and relation names — so it is a signal of the same kind as
/// <see cref="GroundingOverlapCheck"/>: a name being present does not mean a query would return
/// the right answer, but a name being <em>absent</em> does mean no query can. An unreachable
/// question points at a missing entity or relation, which is what makes it worth reporting
/// per run. The questions live here rather than in <c>Eyu.Core</c> on purpose: they are a
/// property of a catalog domain, not of the library, and this is the only consumer.
/// </para>
/// <para>
/// Both record regimes are measured. The catalog's record cases run with the options above; its
/// document cases (<see cref="QualityCatalog.DocumentCases"/> — chunks of one document, which
/// name many entities and denote none) run with the same options but
/// <see cref="LinkageOptions.RecordsDenoteEntities"/> off, which is how a caller holding text
/// would call the proposer. Their pre-filter row says the chunks were not compared, since no pair
/// is, and the report's case table names the regime each row was measured in. Each document case
/// is measured twice — once with nothing declared and once with its declared vocabulary
/// (<see cref="DocumentCase.Vocabulary"/>) — and a table of entity types reports, for both rows,
/// how many entities were stamped declared, how many entity names came back under more than one
/// type across attempts, and how many types were written in Hangul.
/// </para>
/// </summary>
public class EyuOntologyProposerQualityMeasurement(ITestOutputHelper output)
{
    private const int DefaultRuns = 2;

    private static readonly JsonSerializerOptions StructuredLogJsonOptions = new() { WriteIndented = true };

    private sealed class CaseStats
    {
        public int Attempts;
        public int ParseFailures;

        /// <summary>Attempts whose model call itself failed — a timeout, a dropped connection. Not a parse failure: nothing came back to parse.</summary>
        public int ModelFailures;

        /// <summary>Elements the proposer left out of parsed attempts, by reason — the per-element failures a whole-response parse count no longer sees.</summary>
        public readonly SortedDictionary<RejectionReason, int> Rejections = [];
        public int GroundingViolations;
        public int OverlapViolations;
        public readonly List<int> EntityCounts = [];
        public readonly List<int> RelationCounts = [];
        public readonly HashSet<string> EntityTypeShapes = [];

        /// <summary>Question text -> how many parsed attempts produced a vocabulary that reaches it.</summary>
        public readonly Dictionary<string, int> QuestionsReached = [];

        /// <summary>Parsed attempts, the denominator the answerability counts are out of.</summary>
        public int ScoredAttempts;
        public readonly List<string> FailureNotes = [];
        public LinkageAnalysis? Linkage;

        /// <summary>The regime the case was measured in: records that each denote an entity, or chunks of a document.</summary>
        public bool RecordsDenoteEntities = true;

        /// <summary>Whether the case's vocabulary was declared for this row — a document case is measured both ways.</summary>
        public bool VocabularyDeclared;

        /// <summary>Entities across parsed attempts, and how many of them the declared-structure merge stamped <see cref="ProposalBasis.Declared"/>.</summary>
        public int EntitiesProposed;
        public int EntitiesDeclared;

        /// <summary>Entity name (as written) -> every entity type it was proposed under, compared leniently — one name under several types is the drift a declared vocabulary is meant to stop.</summary>
        public readonly Dictionary<string, HashSet<string>> TypesByEntityName = new(StringComparer.Ordinal);

        /// <summary>Entity type (as the model wrote it) -> how many entities carried it.</summary>
        public readonly SortedDictionary<string, int> EntityTypeCounts = new(StringComparer.Ordinal);

        /// <summary>Entities whose type name is written in Hangul — a type name the English innate vocabulary and English declarations can never match.</summary>
        public int HangulTypedEntities;

        /// <summary>
        /// Confidence of every entity that cites a single record, and of every entity citing several, split by what it
        /// claims about them: denoted by at most one (the rest only mention it — a plant, a make, a machine named in a
        /// field), or denoted by several the pre-filter did or did not link. Only the last two are merge claims, so only
        /// they are adjusted; an unlinked one is a merge the linkage evidence argues against.
        /// </summary>
        public readonly List<double> SingleRecordConfidences = [];
        public readonly List<double> ReferencedConfidences = [];
        public readonly List<double> LinkedRecordsConfidences = [];
        public readonly List<double> UnlinkedRecordsConfidences = [];
        public readonly List<string> UnlinkedRecordsEntities = [];

        /// <summary>Entities whose name reads as a field value (<see cref="ValueLikeName"/>) — a date or a number — and the long names listed for review.</summary>
        public int DateNamedEntities;
        public int NumberNamedEntities;
        public readonly List<string> ValueNamedEntities = [];

        /// <summary>
        /// Per parsed attempt: entity name (compared leniently) -> the keys an identifier could be minted from — the
        /// model's <c>EntityId</c>, the records the entity claims denote it, and every record it cites. Read across
        /// attempts to see which of them stays put when the same records are proposed again; an entity named twice in
        /// one attempt keeps its first occurrence.
        /// </summary>
        public readonly List<Dictionary<string, IdentityKeys>> IdentityByAttempt = [];
        public int ProposalLocalIris;
        public int MergedInProposal;
        public int SharedDenotingSets;
        public readonly SortedDictionary<string, int> DenotedBySizes = new(StringComparer.Ordinal);
        public readonly List<string> LongNamedEntities = [];
    }

    private static LinkageOptions BuildLinkageOptionsFromEnvironment()
    {
        var defaults = LinkageOptions.Default;
        return new LinkageOptions(
            MatchThreshold: ParseDouble("EYU_LLM_QUALITY_MATCH_THRESHOLD", defaults.MatchThreshold),
            NonMatchThreshold: ParseDouble("EYU_LLM_QUALITY_NONMATCH_THRESHOLD", defaults.NonMatchThreshold),
            MaxIterations: ParseInt("EYU_LLM_QUALITY_MAX_ITERATIONS", defaults.MaxIterations),
            ConvergenceTolerance: ParseDouble("EYU_LLM_QUALITY_CONVERGENCE_TOLERANCE", defaults.ConvergenceTolerance));

        static double ParseDouble(string variable, double fallback) =>
            double.TryParse(Environment.GetEnvironmentVariable(variable), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;

        static int ParseInt(string variable, int fallback) =>
            int.TryParse(Environment.GetEnvironmentVariable(variable), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;
    }

    [Fact]
    public async Task Measure_proposal_quality_across_the_catalog()
    {
        var runs = int.TryParse(Environment.GetEnvironmentVariable("EYU_LLM_QUALITY_RUNS"), out var parsed) && parsed > 0
            ? parsed
            : DefaultRuns;
        var linkageOptions = BuildLinkageOptionsFromEnvironment();
        var (httpClient, modelClient) = EyuLlmLiveClient.Create();
        using var _ = httpClient;
        var proposer = new SinglePassOntologyProposer(modelClient, linkageOptions);

        var selected = QualityCatalog.SelectedCaseNames();
        var stats = new Dictionary<string, CaseStats>();
        foreach (var qualityCase in QualityCatalog.Cases.Where(c => selected is null || selected.Contains(c.Name)))
        {
            var caseStats = stats[qualityCase.Name] = new CaseStats
            {
                Linkage = LinkagePipeline.Analyze(qualityCase.Records, linkageOptions),
            };
            for (var attempt = 0; attempt < runs; attempt++)
            {
                caseStats.Attempts++;
                await RunAttemptAsync(proposer, [], qualityCase.Records, qualityCase.Questions, caseStats);
            }
        }

        var documentOptions = linkageOptions with { RecordsDenoteEntities = false };
        var documentProposer = new SinglePassOntologyProposer(modelClient, documentOptions);
        foreach (var documentCase in QualityCatalog.DocumentCases.Where(c => selected is null || selected.Contains(c.Name)))
        {
            // The undeclared row keeps the case's own name, so it still compares with runs made
            // before the declared row existed.
            foreach (var declared in new[] { false, true })
            {
                var caseStats = stats[declared ? $"{documentCase.Name} + declared vocabulary" : documentCase.Name] = new CaseStats
                {
                    Linkage = LinkagePipeline.Analyze(documentCase.Chunks, documentOptions),
                    RecordsDenoteEntities = false,
                    VocabularyDeclared = declared,
                };
                for (var attempt = 0; attempt < runs; attempt++)
                {
                    caseStats.Attempts++;
                    await RunAttemptAsync(documentProposer, declared ? documentCase.Vocabulary : [], documentCase.Chunks, documentCase.Questions, caseStats);
                }
            }
        }

        var report = RenderReport(runs, linkageOptions, stats);
        output.WriteLine(report);
        var reportPath = Environment.GetEnvironmentVariable("EYU_LLM_QUALITY_REPORT");
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            await File.WriteAllTextAsync(reportPath, report, TestContext.Current.CancellationToken);
        }

        var structuredLogDir = Environment.GetEnvironmentVariable("EYU_LLM_QUALITY_STRUCTURED_LOG_DIR");
        if (!string.IsNullOrWhiteSpace(structuredLogDir))
        {
            var timestamp = DateTimeOffset.UtcNow;
            Directory.CreateDirectory(structuredLogDir);
            var logPath = Path.Combine(structuredLogDir, $"run-{timestamp:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(logPath, BuildStructuredLogJson(timestamp, runs, linkageOptions, stats), TestContext.Current.CancellationToken);
        }

        // The instrument's own sanity floor, not a graduation gate: a run where nothing ever
        // parsed measured the transport, not the model.
        Assert.True(stats.Values.Sum(s => s.Attempts - s.ParseFailures - s.ModelFailures) > 0,
            "at least one proposal must survive parsing for the measurement to mean anything");
    }

    private static async Task RunAttemptAsync(SinglePassOntologyProposer proposer, IReadOnlyList<DeclaredStructure> declared, RawRecord[] records, CompetencyQuestion[] questions, CaseStats stats)
    {
        var recordIds = records.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        OntologyProposal proposal;
        try
        {
            proposal = await proposer.ProposeAsync(declared, records);
        }
        catch (FormatException ex)
        {
            stats.ParseFailures++;
            stats.FailureNotes.Add(ex.Message);
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            // One slow or dropped call is one failed attempt. Letting it escape ended the whole round
            // and threw away every attempt already measured.
            stats.ModelFailures++;
            stats.FailureNotes.Add($"model call failed: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        foreach (var rejection in proposal.Rejections)
        {
            stats.Rejections[rejection.Reason] = stats.Rejections.GetValueOrDefault(rejection.Reason) + 1;
            stats.FailureNotes.Add($"rejected {rejection.Reason}: {rejection.Detail}");
        }

        stats.ScoredAttempts++;
        RecordIdentity(proposal, stats);
        ScoreCompetencyQuestions(proposal, questions, stats);

        stats.EntityCounts.Add(proposal.Entities.Count);
        stats.RelationCounts.Add(proposal.Relations.Count);
        stats.EntityTypeShapes.Add(string.Join(",", proposal.Entities.Select(e => e.EntityType).Distinct().OrderBy(t => t, StringComparer.Ordinal)));
        ScoreTypeVocabulary(proposal, stats);

        RecordConfidence(proposal, stats);

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
                // claim to actually back it — the gap id-validity alone does not catch.
                // Ratio + claim text recorded so a human reviewing the report
                // can tell a genuine mismatch apart from framing language diluting the ratio
                // (heuristic, not semantic — see GroundingOverlapCheck's doc comment).
                var overlap = GroundingOverlapCheck.Evaluate(entity.Claim, records);
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
                var overlap = GroundingOverlapCheck.Evaluate(relation.Claim, records);
                if (!overlap.IsSupported)
                {
                    stats.OverlapViolations++;
                    stats.FailureNotes.Add(
                        $"relation {relation.RelationName} overlap {overlap.MatchedTokenCount}/{overlap.ClaimTokenCount} ({overlap.Ratio:P0}): \"{relation.Claim.Claim}\"");
                }
            }
        }
    }

    internal sealed record IdentityKeys(string EntityId, string Iri, string DenotedBy, string Cited, string EntityType, int SharingDenotedBy)
    {
        /// <summary>Whether the individual was keyed (name, type and any denoting records) or fell back to its own id.</summary>
        public bool ProposalLocal => Iri.Contains("/local/", StringComparison.Ordinal);
    }

    private static readonly RdfExportOptions IdentityExport = new(new Uri("https://example.org/measurement#"));

    private static void RecordIdentity(OntologyProposal proposal, CaseStats stats)
    {
        var iris = OntologyTurtle.IndividualIris(proposal, IdentityExport);
        stats.ProposalLocalIris += proposal.Entities.Count(e => iris[e.EntityId].Contains("/local/", StringComparison.Ordinal));
        stats.MergedInProposal += proposal.Entities
            .GroupBy(e => iris[e.EntityId], StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Sum(g => g.Count());

        string DenotingSet(EntityProposal e) => string.Join(",", e.DenotedBy.Order(StringComparer.Ordinal));
        var sharing = proposal.Entities
            .Where(e => e.DenotedBy.Count > 0)
            .GroupBy(DenotingSet, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        foreach (var entity in proposal.Entities)
        {
            var size = entity.DenotedBy.Count >= 2 ? "2+" : entity.DenotedBy.Count.ToString(CultureInfo.InvariantCulture);
            stats.DenotedBySizes[size] = stats.DenotedBySizes.GetValueOrDefault(size) + 1;
        }

        stats.SharedDenotingSets += sharing.Count(kv => kv.Value > 1);

        var byName = new Dictionary<string, IdentityKeys>(StringComparer.Ordinal);
        foreach (var entity in proposal.Entities)
        {
            byName.TryAdd(
                LenientTypeName(entity.Name),
                new IdentityKeys(
                    entity.EntityId,
                    iris[entity.EntityId],
                    string.Join(",", entity.DenotedBy.Order(StringComparer.Ordinal)),
                    string.Join(",", entity.Claim.Sources.Select(s => s.RecordId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)),
                    entity.EntityType,
                    entity.DenotedBy.Count > 0 ? sharing[DenotingSet(entity)] : 0));
        }

        stats.IdentityByAttempt.Add(byName);
    }

    /// <summary>
    /// Of the entity names that recur in every parsed attempt, how many kept each key identical across all of them —
    /// the question behind minting an individual's IRI from <c>EntityId</c> (today) or from its denoting records.
    /// </summary>
    private static (int Recurring, int SameId, int SameIri, int SameDenotedBy, int SameCited, int WithDenotedBy) IdentityStability(CaseStats stats)
    {
        var attempts = stats.IdentityByAttempt;
        if (attempts.Count < 2)
        {
            return (0, 0, 0, 0, 0, 0);
        }

        var recurring = attempts.Skip(1).Aggregate(attempts[0].Keys.ToHashSet(StringComparer.Ordinal), (set, next) =>
        {
            set.IntersectWith(next.Keys);
            return set;
        });

        bool Stable(string name, Func<IdentityKeys, string> key) => attempts.Select(a => key(a[name])).Distinct(StringComparer.Ordinal).Count() == 1;

        return (
            recurring.Count,
            recurring.Count(n => Stable(n, k => k.EntityId)),
            recurring.Count(n => Stable(n, k => k.Iri)),
            recurring.Count(n => Stable(n, k => k.DenotedBy)),
            recurring.Count(n => Stable(n, k => k.Cited)),
            recurring.Count(n => attempts.All(a => a[n].DenotedBy.Length > 0)));
    }

    /// <summary>
    /// For each recurring name whose IRI did not hold across attempts, why: the rule that named it changed
    /// between attempts, its denoting records differed, its type was spelled differently, or a key collision
    /// sent it to a proposal-local IRI. The table counts; this says which lever moved.
    /// </summary>
    private static IEnumerable<string> IriLosses(CaseStats stats)
    {
        var attempts = stats.IdentityByAttempt;
        if (attempts.Count < 2)
        {
            yield break;
        }

        var recurring = attempts.Skip(1).Aggregate(attempts[0].Keys.ToHashSet(StringComparer.Ordinal), (set, next) =>
        {
            set.IntersectWith(next.Keys);
            return set;
        });

        foreach (var name in recurring.Order(StringComparer.Ordinal))
        {
            var keys = attempts.Select(a => a[name]).ToList();
            if (keys.Select(k => k.Iri).Distinct(StringComparer.Ordinal).Count() == 1)
            {
                continue;
            }

            var local = keys.Count(k => k.ProposalLocal);
            var differing = new List<string>();
            if (keys.Select(k => LenientTypeName(k.EntityType)).Distinct(StringComparer.Ordinal).Count() > 1)
            {
                differing.Add("type " + string.Join(" / ", keys.Select(k => k.EntityType)));
            }

            if (keys.Select(k => k.DenotedBy).Distinct(StringComparer.Ordinal).Count() > 1)
            {
                differing.Add("denoting records " + string.Join(" / ", keys.Select(k => $"{{{k.DenotedBy}}}")));
            }

            var cause = local > 0
                ? $"proposal-local in {local} of {keys.Count} attempts"
                : differing.Count > 0 ? "differs: " + string.Join("; ", differing) : "name spelled differently";
            yield return $"\"{name}\" — {cause}";
        }
    }

    private static void RecordConfidence(OntologyProposal proposal, CaseStats stats)
    {
        if (!stats.RecordsDenoteEntities || stats.Linkage is null)
        {
            return;
        }

        foreach (var entity in proposal.Entities)
        {
            var cited = entity.Claim.Sources.Select(s => s.RecordId).ToHashSet(StringComparer.Ordinal);
            var denoting = entity.DenotedBy.ToHashSet(StringComparer.Ordinal);
            if (cited.Count <= 1)
            {
                stats.SingleRecordConfidences.Add(entity.Confidence);
            }
            else if (denoting.Count <= 1)
            {
                stats.ReferencedConfidences.Add(entity.Confidence);
            }
            else if (stats.Linkage.Clustering.Clusters.Any(c => denoting.IsSubsetOf(c.RecordIds)))
            {
                stats.LinkedRecordsConfidences.Add(entity.Confidence);
            }
            else
            {
                stats.UnlinkedRecordsConfidences.Add(entity.Confidence);
                stats.UnlinkedRecordsEntities.Add(
                    string.Create(CultureInfo.InvariantCulture, $"{entity.Name} ({entity.EntityType}, denoted by {denoting.Count} of {cited.Count} records) {entity.Confidence:F3}"));
            }
        }
    }

    private static void ScoreCompetencyQuestions(OntologyProposal proposal, CompetencyQuestion[] questions, CaseStats stats)
    {
        var vocabulary = CompetencyQuestionReach.Vocabulary(proposal);

        foreach (var question in questions)
        {
            stats.QuestionsReached.TryAdd(question.Question, 0);
            if (CompetencyQuestionReach.Reaches(question, vocabulary))
            {
                stats.QuestionsReached[question.Question]++;
            }
            else
            {
                stats.FailureNotes.Add(
                    $"unreachable question \"{question.Question}\" — vocabulary was [{string.Join(", ", vocabulary.Distinct())}]"
                    + RenderReachedConcepts(CompetencyQuestionReach.ReachedConcepts(question, proposal)));
            }
        }
    }

    private static void ScoreTypeVocabulary(OntologyProposal proposal, CaseStats stats)
    {
        foreach (var entity in proposal.Entities)
        {
            stats.EntitiesProposed++;
            if (entity.Basis == ProposalBasis.Declared)
            {
                stats.EntitiesDeclared++;
            }

            if (entity.EntityType.Any(IsHangul))
            {
                stats.HangulTypedEntities++;
            }

            stats.EntityTypeCounts[entity.EntityType] = stats.EntityTypeCounts.GetValueOrDefault(entity.EntityType) + 1;
            var name = entity.Name.Trim();
            switch (ValueLikeName.Classify(name))
            {
                case ValueLikeName.Kind.Date:
                    stats.DateNamedEntities++;
                    stats.ValueNamedEntities.Add($"{name} ({entity.EntityType})");
                    break;
                case ValueLikeName.Kind.Number:
                    stats.NumberNamedEntities++;
                    stats.ValueNamedEntities.Add($"{name} ({entity.EntityType})");
                    break;
                default:
                    if (ValueLikeName.IsLong(name))
                    {
                        stats.LongNamedEntities.Add($"{name} ({entity.EntityType})");
                    }

                    break;
            }

            if (!stats.TypesByEntityName.TryGetValue(name, out var types))
            {
                stats.TypesByEntityName[name] = types = new HashSet<string>(StringComparer.Ordinal);
            }

            types.Add(LenientTypeName(entity.EntityType));
        }
    }

    /// <summary>
    /// The same leniency the declared-structure merge applies to type names — case and separators
    /// ignored — so <c>WorkOrder</c> and <c>work_order</c> do not count as drift here either.
    /// </summary>
    private static string LenientTypeName(string type)
        => new(type.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static bool IsHangul(char c) => c is >= '가' and <= '힣' or >= 'ᄀ' and <= 'ᇿ' or >= '㄰' and <= '㆏';

    private static int NamesUnderSeveralTypes(CaseStats stats) => stats.TypesByEntityName.Count(kv => kv.Value.Count > 1);

    /// <summary>How many stand-in entities one concept is worth printing before the note stops being read.</summary>
    private const int StandInsPerConcept = 2;

    /// <summary>
    /// The tail of an unreachable-question note: what the proposal built where the missing concept
    /// belonged. Without it the note lists a vocabulary and leaves the reader to work out whether
    /// the model skipped the idea or expressed it another way, which is the part worth knowing.
    /// </summary>
    private static string RenderReachedConcepts(IReadOnlyList<ReachedConcept> reached)
    {
        if (reached.Count == 0)
        {
            return string.Empty;
        }

        var parts = reached.Select(concept => concept.Entities.Count == 0
            ? $"reached \"{concept.Concept}\" through a relation name only"
            : $"reached \"{concept.Concept}\" through "
                + string.Join("; ", concept.Entities
                    .Take(StandInsPerConcept)
                    .Select(entity => $"{entity.EntityType} {entity.EntityId} (\"{entity.Claim.Claim}\")"))
                + (concept.Entities.Count > StandInsPerConcept
                    ? $" (+{concept.Entities.Count - StandInsPerConcept} more)"
                    : string.Empty));

        return " — " + string.Join(", ", parts);
    }

    private static string RenderReport(int runs, LinkageOptions linkageOptions, Dictionary<string, CaseStats> stats)
    {
        var report = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"# SinglePassOntologyProposer quality measurement — {runs} runs/case")
            .AppendLine(CultureInfo.InvariantCulture, $"Prompt {SinglePassOntologyProposer.PromptFingerprint} (fixed preamble + declaration clause + response schema; changes when that wording or schema changes — runs under different fingerprints are not comparable).")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture,
                $"LinkageOptions: MatchThreshold={linkageOptions.MatchThreshold}, NonMatchThreshold={linkageOptions.NonMatchThreshold}, " +
                $"MaxIterations={linkageOptions.MaxIterations}, ConvergenceTolerance={linkageOptions.ConvergenceTolerance}")
            .AppendLine()
            .AppendLine("| case | records | parse ok | rejected elements (by reason) | grounding violations | overlap violations | entities (min-max) | relations (min-max) | distinct type-shapes |")
            .AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var (name, s) in stats)
        {
            var parseOk = s.Attempts - s.ParseFailures - s.ModelFailures;
            var entityRange = s.EntityCounts.Count == 0 ? "n/a" : $"{s.EntityCounts.Min()}-{s.EntityCounts.Max()}";
            var relationRange = s.RelationCounts.Count == 0 ? "n/a" : $"{s.RelationCounts.Min()}-{s.RelationCounts.Max()}";
            report.AppendLine(CultureInfo.InvariantCulture,
                $"| {name} | {Regime(s)} | {parseOk}/{s.Attempts}{(s.ModelFailures > 0 ? $" ({s.ModelFailures} model call failed)" : "")} | {RenderRejections(s.Rejections)} | {s.GroundingViolations} | {s.OverlapViolations} | {entityRange} | {relationRange} | {s.EntityTypeShapes.Count} |");
        }

        report.AppendLine().AppendLine("## Fellegi-Sunter pre-filter (per case, independent of model attempts)")
            .AppendLine()
            .AppendLine("| case | pairs | match | gray-zone | non-match | EM status | match prior | est. false-match | est. false-non-match | estimate caveats |")
            .AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (var (name, s) in stats)
        {
            var linkage = s.Linkage;
            if (linkage is null || linkage.Parameters is null)
            {
                var reason = s.RecordsDenoteEntities ? "fewer than 2 records" : "document chunks are not compared";
                report.AppendLine(CultureInfo.InvariantCulture, $"| {name} | 0 | - | - | - | n/a ({reason}) | n/a | n/a | n/a | n/a |");
                continue;
            }

            var match = linkage.PairLinkages.Count(p => p.Classification == LinkageClassification.Match);
            var grayZone = linkage.PairLinkages.Count(p => p.Classification == LinkageClassification.GrayZone);
            var nonMatch = linkage.PairLinkages.Count(p => p.Classification == LinkageClassification.NonMatch);
            var errorRates = linkage.ErrorRates;
            report.AppendLine(CultureInfo.InvariantCulture,
                $"| {name} | {linkage.PairLinkages.Count} | {match} | {grayZone} | {nonMatch} | {linkage.Parameters.Status}{(linkage.Parameters.LabelsSwapped ? " (relabeled)" : "")} | {linkage.Parameters.MatchPrior:F3} | {errorRates?.FalseMatchRate.ToString("F3", CultureInfo.InvariantCulture) ?? "n/a"} | {errorRates?.FalseNonMatchRate.ToString("F3", CultureInfo.InvariantCulture) ?? "n/a"} | {(errorRates is null ? "n/a" : errorRates.IsReliable ? "none" : errorRates.Caveats.ToString())} |");
        }

        var documentRows = stats.Where(kv => !kv.Value.RecordsDenoteEntities).ToList();
        if (documentRows.Count > 0)
        {
            report.AppendLine().AppendLine("## Entity type vocabulary (document cases, across parsed attempts)")
                .AppendLine()
                .AppendLine("One entity name proposed under more than one type (compared ignoring case and separators) is the drift a declared vocabulary is meant to stop; a Hangul type name can match neither the innate vocabulary nor an English declaration.")
                .AppendLine()
                .AppendLine("| case | vocabulary declared | entities | stamped Declared | names under several types | distinct types | Hangul-typed entities |")
                .AppendLine("|---|---|---|---|---|---|---|");
            foreach (var (name, s) in documentRows)
            {
                report.AppendLine(CultureInfo.InvariantCulture,
                    $"| {name} | {(s.VocabularyDeclared ? "yes" : "no")} | {s.EntitiesProposed} | {s.EntitiesDeclared} | {NamesUnderSeveralTypes(s)}/{s.TypesByEntityName.Count} | {s.EntityTypeCounts.Count} | {s.HangulTypedEntities} |");
            }

            report.AppendLine();
            foreach (var (name, s) in documentRows)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"- {name} types: {string.Join(", ", s.EntityTypeCounts.Select(kv => $"{kv.Key} {kv.Value}"))}");
                foreach (var (entityName, types) in s.TypesByEntityName.Where(kv => kv.Value.Count > 1).OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    report.AppendLine(CultureInfo.InvariantCulture, $"  - \"{entityName}\" under {string.Join(" / ", types.Order(StringComparer.Ordinal))}");
                }
            }
        }

        report.AppendLine().AppendLine("## Entity identity across attempts (same records, proposed again)")
            .AppendLine()
            .AppendLine("An entity is followed from attempt to attempt by its name, compared leniently — a heuristic join, since a renamed entity drops out. For each name present in every parsed attempt: did the model's id stay the same, did the IRI `Eyu.Rdf` writes its individual under (derived from its denoting records, else its name and type), did the records it claims denote it, did the records it cites? A key that holds across attempts is one a consumer merging two exports could join on. Merged counts entities, over all parsed attempts, that shared one individual with another entity of the same proposal (one name and type, no denoting record — a duplicate the model proposed once per chunk); proposal-local counts entities written under their own id because another entity of the proposal claimed the very same denoting records.")
            .AppendLine()
            .AppendLine("| case | names in every attempt | same id | same IRI | same denotedBy | same cited records | denoted by ≥1 record in every attempt | merged in one proposal | proposal-local IRIs |")
            .AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var (name, s) in stats)
        {
            var (recurring, sameId, sameIri, sameDenotedBy, sameCited, withDenotedBy) = IdentityStability(s);
            report.AppendLine(CultureInfo.InvariantCulture,
                $"| {name} | {recurring} | {sameId} | {sameIri} | {sameDenotedBy} | {sameCited} | {withDenotedBy} | {s.MergedInProposal}/{s.EntitiesProposed} | {s.ProposalLocalIris}/{s.EntitiesProposed} |");
        }

        report.AppendLine();
        foreach (var (name, s) in stats)
        {
            report.AppendLine(CultureInfo.InvariantCulture,
                $"- {name} denotedBy sizes: {string.Join(", ", s.DenotedBySizes.Select(kv => $"{kv.Key} → {kv.Value}"))}; denoting sets several entities claimed, over all attempts: {s.SharedDenotingSets}");
            foreach (var loss in IriLosses(s))
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"- {name} IRI lost: {loss}");
            }
        }

        var recordRows = stats.Where(kv => kv.Value.RecordsDenoteEntities && kv.Value.Linkage is not null).ToList();
        if (recordRows.Count > 0)
        {
            report.AppendLine().AppendLine("## Entity confidence by what the cited records are (records cases, across parsed attempts)")
                .AppendLine()
                .AppendLine("An entity citing several records is split by what it claims about them. Denoted by at most one, the rest only mention it (a plant, a make, a machine named in a field) and nothing is adjusted. Denoted by several, it claims they are one entity, and the confidence adjustment weighs that claim against the linkage evidence — linked when the pre-filter agrees, unlinked when it does not (listed below).")
                .AppendLine()
                .AppendLine("| case | one record: n · mean | referenced (denoted by ≤1): n · mean | denoted, linked: n · mean | denoted, unlinked: n · mean |")
                .AppendLine("|---|---|---|---|---|");
            foreach (var (name, s) in recordRows)
            {
                report.AppendLine(CultureInfo.InvariantCulture,
                    $"| {name} | {Summary(s.SingleRecordConfidences)} | {Summary(s.ReferencedConfidences)} | {Summary(s.LinkedRecordsConfidences)} | {Summary(s.UnlinkedRecordsConfidences)} |");
            }

            report.AppendLine();
            foreach (var (name, s) in recordRows)
            {
                foreach (var entity in s.UnlinkedRecordsEntities.Distinct())
                {
                    report.AppendLine(CultureInfo.InvariantCulture, $"- {name}: {entity}");
                }
            }
        }

        report.AppendLine().AppendLine("## Entities named by a value (across parsed attempts)")
            .AppendLine()
            .AppendLine("A proposal has no attribute channel, so a record's dates and quantities can only come back as entities. Counted: names that are a date or a number with at most a short unit. Listed, not counted: names of six words or more, which may be a free-text value or may be an event's title.")
            .AppendLine()
            .AppendLine("| case | entities | date-named | number-named | value-named share | long names (review) |")
            .AppendLine("|---|---|---|---|---|---|");
        foreach (var (name, s) in stats)
        {
            var valueNamed = s.DateNamedEntities + s.NumberNamedEntities;
            var share = s.EntitiesProposed == 0 ? "-" : ((double)valueNamed / s.EntitiesProposed).ToString("P0", CultureInfo.InvariantCulture);
            report.AppendLine(CultureInfo.InvariantCulture,
                $"| {name} | {s.EntitiesProposed} | {s.DateNamedEntities} | {s.NumberNamedEntities} | {share} | {s.LongNamedEntities.Count} |");
        }

        report.AppendLine();
        foreach (var (name, s) in stats)
        {
            foreach (var entity in s.ValueNamedEntities.Distinct())
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"- {name} value: {entity}");
            }

            foreach (var entity in s.LongNamedEntities.Distinct())
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"- {name} long: {entity}");
            }
        }

        report.AppendLine().AppendLine("## Competency questions (vocabulary reach — heuristic, not a semantic check)")
            .AppendLine()
            .AppendLine("| case | question | reached |")
            .AppendLine("|---|---|---|");
        foreach (var (name, s) in stats)
        {
            foreach (var (question, reached) in s.QuestionsReached)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"| {name} | {question} | {reached}/{s.ScoredAttempts} |");
            }
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

    private static string Summary(List<double> values) =>
        values.Count == 0 ? "0 · -" : string.Create(CultureInfo.InvariantCulture, $"{values.Count} · {values.Average():F3}");

    private static string Regime(CaseStats stats) => stats.RecordsDenoteEntities ? "entities" : "document chunks";

    private static string RenderRejections(SortedDictionary<RejectionReason, int> rejections) =>
        rejections.Count == 0 ? "0" : string.Join(", ", rejections.Select(kv => $"{kv.Key} {kv.Value}"));

    // One JSON file per run, named by timestamp -- a git-friendly run-history directory that can
    // be listed, diffed and scripted over, instead of re-reading prose reports to compare runs.
    // Field set mirrors RenderReport's two tables so both stay in sync by construction.
    private sealed record LinkageOptionsSnapshot(
        double MatchThreshold,
        double NonMatchThreshold,
        int MaxIterations,
        double ConvergenceTolerance,
        bool UseStringSimilarityComparator,
        double StringSimilarityAgreementThreshold);

    private sealed record LinkageSnapshot(
        int Pairs,
        int Match,
        int GrayZone,
        int NonMatch,
        string EstimationStatus,
        double? MatchPrior,
        double? EstimatedFalseMatchRate,
        double? EstimatedFalseNonMatchRate,
        string? ErrorRateCaveats);

    private sealed record QuestionLogEntry(string Question, int Reached, int ScoredAttempts);

    private sealed record CaseLogEntry(
        string Name,
        bool RecordsDenoteEntities,
        int Attempts,
        int ParseFailures,
        IReadOnlyDictionary<string, int> RejectionsByReason,
        int GroundingViolations,
        int OverlapViolations,
        int? EntityCountMin,
        int? EntityCountMax,
        int? RelationCountMin,
        int? RelationCountMax,
        int DistinctEntityTypeShapes,
        IReadOnlyList<QuestionLogEntry> Questions,
        LinkageSnapshot? Linkage,
        bool VocabularyDeclared,
        int EntitiesProposed,
        int EntitiesDeclared,
        int NamesUnderSeveralTypes,
        int DistinctEntityNames,
        IReadOnlyDictionary<string, int> EntityTypeCounts,
        int HangulTypedEntities);

    private sealed record StructuredLogEntry(
        string Timestamp,
        int Runs,
        LinkageOptionsSnapshot LinkageOptions,
        IReadOnlyList<CaseLogEntry> Cases);

    private static string BuildStructuredLogJson(
        DateTimeOffset timestamp, int runs, LinkageOptions linkageOptions, Dictionary<string, CaseStats> stats)
    {
        var cases = stats.Select(kv =>
        {
            var (name, s) = (kv.Key, kv.Value);
            var linkage = s.Linkage;
            var linkageSnapshot = linkage?.Parameters is null
                ? null
                : new LinkageSnapshot(
                    linkage.PairLinkages.Count,
                    linkage.PairLinkages.Count(p => p.Classification == LinkageClassification.Match),
                    linkage.PairLinkages.Count(p => p.Classification == LinkageClassification.GrayZone),
                    linkage.PairLinkages.Count(p => p.Classification == LinkageClassification.NonMatch),
                    linkage.Parameters.Status + (linkage.Parameters.LabelsSwapped ? "(relabeled)" : ""),
                    linkage.Parameters.MatchPrior,
                    linkage.ErrorRates?.FalseMatchRate,
                    linkage.ErrorRates?.FalseNonMatchRate,
                    linkage.ErrorRates is null ? null : linkage.ErrorRates.IsReliable ? "none" : linkage.ErrorRates.Caveats.ToString());

            return new CaseLogEntry(
                name,
                s.RecordsDenoteEntities,
                s.Attempts,
                s.ParseFailures,
                s.Rejections.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
                s.GroundingViolations,
                s.OverlapViolations,
                s.EntityCounts.Count == 0 ? null : s.EntityCounts.Min(),
                s.EntityCounts.Count == 0 ? null : s.EntityCounts.Max(),
                s.RelationCounts.Count == 0 ? null : s.RelationCounts.Min(),
                s.RelationCounts.Count == 0 ? null : s.RelationCounts.Max(),
                s.EntityTypeShapes.Count,
                [.. s.QuestionsReached.Select(q => new QuestionLogEntry(q.Key, q.Value, s.ScoredAttempts))],
                linkageSnapshot,
                s.VocabularyDeclared,
                s.EntitiesProposed,
                s.EntitiesDeclared,
                NamesUnderSeveralTypes(s),
                s.TypesByEntityName.Count,
                s.EntityTypeCounts,
                s.HangulTypedEntities);
        }).ToList();

        var entry = new StructuredLogEntry(
            timestamp.ToString("O", CultureInfo.InvariantCulture),
            runs,
            new LinkageOptionsSnapshot(
                linkageOptions.MatchThreshold,
                linkageOptions.NonMatchThreshold,
                linkageOptions.MaxIterations,
                linkageOptions.ConvergenceTolerance,
                linkageOptions.UseStringSimilarityComparator,
                linkageOptions.StringSimilarityAgreementThreshold),
            cases);

        return JsonSerializer.Serialize(entry, StructuredLogJsonOptions);
    }
}
