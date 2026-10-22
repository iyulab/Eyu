using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Eyu.Core.Declared;
using Eyu.Core.Grounding;
using Eyu.Core.Inference;
using Eyu.Core.Linkage;
using Eyu.Core.Ports;
using Eyu.Core.Proposals;
using Eyu.Core.Records;

namespace Eyu.Core.Judgment;

/// <summary>
/// The single-baseline <see cref="IOntologyProposer"/> implementation this library's internal
/// benchmark (a "single baseline" gate) compares against: one prompt, one <see
/// cref="IModelClient"/> call, one parse, with an optional <see cref="LinkageOptions"/> to tune
/// the Fellegi-Sunter pre-filter's classification thresholds and EM iteration limits away from
/// their defaults. Before building the prompt, that pre-filter
/// (<see cref="LinkagePipeline"/>) classifies every record pair as a
/// confirmed match, a confirmed non-match, or a gray-zone case needing the model's judgment.
/// Confirmed matches never use the model's self-reported confidence; gray-zone cases combine the
/// Fellegi-Sunter prior with it via a Bayesian update (<see cref="LinkageConfidenceAdjuster"/>).
/// Either way the adjustment reads only the records an entity claims are <em>itself</em>
/// (<see cref="EntityProposal.DenotedBy"/>), never the records that merely mention it.
/// The parse refuses the whole response only for invalid JSON or an invented source; any other
/// defective element is left out and reported in <see cref="OntologyProposal.Rejections"/>, and
/// every surviving element's <see cref="VocabularyOrigin"/> is stamped from
/// <see cref="InnateVocabulary"/> rather than taken from the model.
/// After the parse, <see cref="DeclaredStructureMerge"/> applies the deterministic half of "Declared
/// always wins": declared types are stamped as such and a relation that misuses a declared name is
/// dropped, so the prompt's authority sentence is a request to the model and the merge is the
/// guarantee to the caller.
/// The call carries the response's JSON Schema in <see cref="ModelRequest.ResponseSchema"/> as well
/// as describing it in the prompt, so a client with structured output can hold the answer to bare
/// JSON; the parse above does not depend on it.
/// The prompt's preamble deliberately does not say what counts as an entity or which kinds should
/// become types rather than instances: that is the innate grammar's question (design rationale, §C
/// and §E), not this class's, so the model's type-versus-instance choices are measured rather than
/// steered, and a test pins the preamble so a steering sentence cannot arrive by accident.
/// This class still only proves the wiring is correct; it makes no claim about judgment quality
/// on its own, which no unit test can verify without a real model behind
/// <see cref="IModelClient"/>.
/// <para>
/// By default each call fits the pre-filter's parameters on its own record pairs. A caller comparing
/// new records against entities it already knows, a few records at a time, fits once on the population
/// instead and passes the result as <c>linkageParameters</c>: every call is then scored with those
/// parameters and runs no estimate (<see cref="LinkagePipeline.Analyze(IReadOnlyList{RawRecord}, IReadOnlyList{KnownEntity}, FieldLinkageParameters, LinkageOptions, Func{RawRecord, string})"/>)
/// — a call of one record against its known entities holds about one true match, too little for an
/// estimate to tell what a match looks like, and the same record would otherwise be judged against a
/// different scale on every call.
/// </para>
/// </summary>
/// <param name="modelClient">The model the proposal is asked of.</param>
/// <param name="linkageOptions">The pre-filter's thresholds and limits; defaults when null.</param>
/// <param name="linkageParameters">
/// Parameters fitted once on the population — <see cref="FieldLinkageParameters.FromNonMatchAgreement"/>, or
/// <see cref="FellegiSunterEstimator.Estimate"/> over pairs where duplicates gather — to score every call
/// with; null fits each call on its own pairs. A field the parameters do not name carries no evidence.
/// </param>
/// <param name="proposeTypeRelations">
/// Whether every call is asked for type relations (<see cref="OntologyProposal.TypeRelations"/>) — which
/// types the records show to be kinds of others. Off, a call is asked for them only when its declarations
/// name a super type (<see cref="DeclaredStructure.SuperTypes"/>), and otherwise sends the request it
/// always sent. Either way the prompt asks only for type relations the records support; it does not say
/// which kinds should be types (design rationale §E).
/// </param>
public sealed partial class SinglePassOntologyProposer(
    IModelClient modelClient,
    LinkageOptions? linkageOptions = null,
    FieldLinkageParameters? linkageParameters = null,
    bool proposeTypeRelations = false) : IOntologyProposer
{
    private const int DiagnosticExcerptLength = 500;

    private readonly LinkageOptions options = linkageOptions ?? LinkageOptions.Default;

    // The prompt's fixed text — the part that is identical across every call, regardless of the
    // declared structure or records handed in. BuildPrompt appends these verbatim; PromptFingerprint
    // hashes them. Keeping the literals here (rather than inline in BuildPrompt) makes them the
    // single source both use, so the fingerprint cannot silently disagree with the prompt.
    private const string PromptInstruction = "Propose entities and relations grounded in the input below.";
    private const string PromptSchema = "Respond with JSON only: {\"entities\":[{\"id\",\"name\",\"type\",\"claim\",\"sources\",\"denotedBy\",\"confidence\"}],\"relations\":[{\"name\",\"from\",\"to\",\"claim\",\"sources\",\"confidence\"}]}.";
    private const string PromptReferenceRule = "An entity's \"id\" only links relations to it within this response; its \"name\" is the entity as the records write it. Every claim must cite at least one source id. In an entity's \"denotedBy\", list the source ids that are records of that entity itself, not those that only refer to it. A source id is always the id of one of the records, never a value written in them.";

    // The same response shape as PromptSchema, as a JSON Schema handed to the model client for
    // structured output. The prompt sentence stays: a client that ignores the schema has only the
    // sentence to go on. A test holds the two to the same field names and holds the parser to
    // accepting a response that fills every one of them, so the three copies cannot drift apart.
    // The schema is strict (every field required, nothing extra); the parser stays tolerant, so a
    // provider that does not enforce it still gets per-element rejections rather than a failure.
    private static readonly JsonElement ResponseSchema = JsonElement.Parse("""
        {
          "type": "object",
          "properties": {
            "entities": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "id": { "type": "string" },
                  "name": { "type": "string" },
                  "type": { "type": "string" },
                  "claim": { "type": "string" },
                  "sources": { "type": "array", "items": { "type": "string" } },
                  "denotedBy": { "type": "array", "items": { "type": "string" } },
                  "confidence": { "type": "number" }
                },
                "required": ["id", "name", "type", "claim", "sources", "denotedBy", "confidence"],
                "additionalProperties": false
              }
            },
            "relations": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "name": { "type": "string" },
                  "from": { "type": "string" },
                  "to": { "type": "string" },
                  "claim": { "type": "string" },
                  "sources": { "type": "array", "items": { "type": "string" } },
                  "confidence": { "type": "number" }
                },
                "required": ["name", "from", "to", "claim", "sources", "confidence"],
                "additionalProperties": false
              }
            }
          },
          "required": ["entities", "relations"],
          "additionalProperties": false
        }
        """);

    // The base schema with the entity's "knownEntityKey" added — required, and null when the entity is
    // new, so a strict provider still has to decide for every entity.
    private static readonly JsonElement ResponseSchemaWithKnownEntities = JsonElement.Parse(
        ResponseSchema.GetRawText()
            .Replace("\"denotedBy\": { \"type\": \"array\", \"items\": { \"type\": \"string\" } },", "\"denotedBy\": { \"type\": \"array\", \"items\": { \"type\": \"string\" } },\n                  \"knownEntityKey\": { \"type\": [\"string\", \"null\"] },", StringComparison.Ordinal)
            .Replace("\"required\": [\"id\", \"name\", \"type\", \"claim\", \"sources\", \"denotedBy\", \"confidence\"]", "\"required\": [\"id\", \"name\", \"type\", \"claim\", \"sources\", \"denotedBy\", \"knownEntityKey\", \"confidence\"]", StringComparison.Ordinal));

    // Two wordings of the floor sentence failed in measurement, each in one regime. With only
    // "beyond what is declared" after the request to use declared types, the model kept document
    // chunks to exactly the declared entity types. Saying the declared types are not the only ones
    // restored that, but adding "wherever nothing declared describes it" closed the form regime
    // instead: a record the declared type already describes left nothing to propose beyond it. The
    // sentence keeps both halves that worked and names no kind of entity (that would be the
    // steering design rationale §E keeps out of the prompt).
    // Only when known entities are supplied: the clause, and the schema sentence that names the field
    // the answer fills. A call without them sends the request it always sent, so its fingerprint and
    // every measurement taken under it stay comparable.
    private const string PromptSchemaWithKnownEntities = "Respond with JSON only: {\"entities\":[{\"id\",\"name\",\"type\",\"claim\",\"sources\",\"denotedBy\",\"knownEntityKey\",\"confidence\"}],\"relations\":[{\"name\",\"from\",\"to\",\"claim\",\"sources\",\"confidence\"}]}.";
    private const string KnownEntitiesClause = "Known entities are ones earlier work already identified, each listed with its key, type, name and the records that denoted it. When an entity you propose is the same thing as a known entity, set its \"knownEntityKey\" to that key; otherwise set it to null. A known entity's type is the name already in use for its kind of thing: an entity of the same kind gets that type too, known or not, rather than a new name for the same kind. Known entities' records are shown for comparison only and cannot be cited as sources.";

    // Only when mentioned entities are supplied: the clause, the schema sentence for the top-level
    // "mergeCandidates" array, and that array in the response schema. A call without them sends the
    // request it sent before, so both fingerprints above and every measurement taken under them hold.
    private const string PromptSchemaMergeCandidates = "Also include in the same object \"mergeCandidates\":[{\"entity\",\"mentionedKey\",\"claim\",\"sources\",\"confidence\"}].";
    private const string MentionedEntitiesClause = "Mentioned entities are ones earlier work saw only named by records that refer to them, never in a record of the thing itself, so what they are is not settled; each is listed with its key, type, name and the records that mentioned it. When an entity you propose is denoted by one of the Records and may be the same thing as a mentioned entity, add a merge candidate: its \"entity\" is your entity's id, its \"mentionedKey\" the mentioned entity's key, and its claim says why, citing only the Records. A mentioned entity's key is never a \"knownEntityKey\". Mentioned entities' records are shown for comparison only and cannot be cited as sources.";

    // Only when type relations are asked for (the proposer's option, or a declared super type): the schema
    // sentence for the top-level "typeRelations" array, the clause, and that array in the response schema.
    // A call without them sends the request it sent before, so every fingerprint above holds. The clause
    // asks for the type relations the records support and names no kind of thing: which kinds become types
    // stays the model's modelling choice (design rationale §E).
    private const string PromptSchemaTypeRelations = "Also include in the same object \"typeRelations\":[{\"sub\",\"super\",\"claim\",\"sources\",\"confidence\"}].";
    private const string TypeRelationsClause = "A type relation says that one type is a kind of another: its \"sub\" is the narrower type and its \"super\" the broader, each a type name as you write types, and its claim says why, citing the Records. Propose a type relation only where the records support it; an entity's own type stays in its \"type\". Answer [] when the records show none.";

    private static readonly JsonElement TypeRelationsSchema = JsonElement.Parse("""
        {
          "type": "array",
          "items": {
            "type": "object",
            "properties": {
              "sub": { "type": "string" },
              "super": { "type": "string" },
              "claim": { "type": "string" },
              "sources": { "type": "array", "items": { "type": "string" } },
              "confidence": { "type": "number" }
            },
            "required": ["sub", "super", "claim", "sources", "confidence"],
            "additionalProperties": false
          }
        }
        """);

    // Only when known types are supplied: the clause and the list. The response shape does not change —
    // the answer still names a type per entity, and the parse reads it against the names in use.
    private const string KnownTypesClause = "Known types are type names earlier work already uses, each listed with records of its kind where there are any. When an entity you propose is of a kind a known type names, give it that type, written as listed, rather than a new name for the same kind; a kind no known type names gets a type of your own. Known types' records are shown for comparison only and cannot be cited as sources.";

    private static readonly JsonElement MergeCandidatesSchema = JsonElement.Parse("""
        {
          "type": "array",
          "items": {
            "type": "object",
            "properties": {
              "entity": { "type": "string" },
              "mentionedKey": { "type": "string" },
              "claim": { "type": "string" },
              "sources": { "type": "array", "items": { "type": "string" } },
              "confidence": { "type": "number" }
            },
            "required": ["entity", "mentionedKey", "claim", "sources", "confidence"],
            "additionalProperties": false
          }
        }
        """);

    private static readonly JsonElement ResponseSchemaWithMentionedEntities = WithMergeCandidates(ResponseSchema);
    private static readonly JsonElement ResponseSchemaWithKnownAndMentionedEntities = WithMergeCandidates(ResponseSchemaWithKnownEntities);

    /// <summary>The schema with the top-level "mergeCandidates" array added — required, so a strict provider answers it (with [] when there is none).</summary>
    private static JsonElement WithMergeCandidates(JsonElement schema) => WithArray(schema, "mergeCandidates", MergeCandidatesSchema);

    /// <summary>The schema with a top-level array added — required, so a strict provider answers it (with [] when there is none).</summary>
    private static JsonElement WithArray(JsonElement schema, string name, JsonElement arraySchema)
    {
        var node = JsonNode.Parse(schema.GetRawText())!.AsObject();
        node["properties"]!.AsObject()[name] = JsonNode.Parse(arraySchema.GetRawText());
        node["required"]!.AsArray().Add((JsonNode)name);
        return JsonElement.Parse(node.ToJsonString());
    }

    private static readonly JsonElement[] ResponseSchemasWithTypeRelations =
    [
        WithArray(ResponseSchema, "typeRelations", TypeRelationsSchema),
        WithArray(ResponseSchemaWithKnownEntities, "typeRelations", TypeRelationsSchema),
        WithArray(ResponseSchemaWithMentionedEntities, "typeRelations", TypeRelationsSchema),
        WithArray(ResponseSchemaWithKnownAndMentionedEntities, "typeRelations", TypeRelationsSchema),
    ];

    private static JsonElement ResponseSchemaFor(bool knownEntities, bool mentionedEntities, bool typeRelations)
    {
        var index = (knownEntities ? 1 : 0) + (mentionedEntities ? 2 : 0);
        return typeRelations
            ? ResponseSchemasWithTypeRelations[index]
            : index switch
            {
                0 => ResponseSchema,
                1 => ResponseSchemaWithKnownEntities,
                2 => ResponseSchemaWithMentionedEntities,
                _ => ResponseSchemaWithKnownAndMentionedEntities,
            };
    }

    private const string DeclarationClause = "Declared structure is authoritative: a declared type, field or relation is fact, not a hypothesis. Where a declared type describes an entity, propose the entity under that type; where a declared relation describes a relation, propose it under that name -- the quoted name alone, not the ends written after it; never contradict declared structure. A declaration is a floor, not a ceiling: the declared types and relations are not the only ones, so still propose every entity and relation the records show beyond what is declared, under types and names of your own.";

    /// <summary>
    /// First 8 hex characters of the SHA-256 of the request's fixed part (preamble + the declaration
    /// clause + the response schema) — a short, stable identifier that changes whenever that wording
    /// or schema changes and stays the same otherwise. A measurement report stamps it so two runs
    /// made across a prompt edit are not read as comparable by accident. It covers only the fixed
    /// part, not the per-call declared structure or records, so the same prompt version yields the
    /// same fingerprint on any input.
    /// </summary>
    public static string PromptFingerprint { get; } = ComputeFingerprint();

    /// <summary>
    /// The fingerprint of the fixed part a call with known entities adds (the known-entities clause and
    /// the response schema that carries <c>knownEntityKey</c>). A report of such a call stamps both;
    /// <see cref="PromptFingerprint"/> alone is unchanged by this feature.
    /// </summary>
    public static string KnownEntitiesPromptFingerprint { get; } = Fingerprint(string.Join('\n', PromptSchemaWithKnownEntities, KnownEntitiesClause, ResponseSchemaWithKnownEntities.GetRawText()));

    /// <summary>
    /// The fingerprint of the fixed part a call with mentioned entities adds (the schema sentence for
    /// merge candidates, the mentioned-entities clause and the array the response schema gains). A
    /// report of such a call stamps it beside the others, which this feature leaves unchanged.
    /// </summary>
    public static string MentionedEntitiesPromptFingerprint { get; } = Fingerprint(string.Join('\n', PromptSchemaMergeCandidates, MentionedEntitiesClause, MergeCandidatesSchema.GetRawText()));

    /// <summary>
    /// The fingerprint of the fixed part a call with known types adds (the known-types clause). A report
    /// of such a call stamps it beside the others, which this feature leaves unchanged.
    /// </summary>
    public static string KnownTypesPromptFingerprint { get; } = Fingerprint(KnownTypesClause);

    /// <summary>
    /// The fingerprint of the fixed part a call asked for type relations adds (the schema sentence, the
    /// type-relations clause and the array the response schema gains). A report of such a call stamps it
    /// beside the others, which this feature leaves unchanged.
    /// </summary>
    public static string TypeRelationsPromptFingerprint { get; } = Fingerprint(string.Join('\n', PromptSchemaTypeRelations, TypeRelationsClause, TypeRelationsSchema.GetRawText()));

    private static string ComputeFingerprint() =>
        Fingerprint(string.Join('\n', PromptInstruction, PromptSchema, PromptReferenceRule, DeclarationClause, ResponseSchema.GetRawText()));

    private static string Fingerprint(string fixedText) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fixedText))).ToLowerInvariant()[..8];

    public Task<OntologyProposal> ProposeAsync(IReadOnlyList<DeclaredStructure> declaredStructures, IReadOnlyList<RawRecord> records, CancellationToken cancellationToken = default)
        => ProposeAsync(declaredStructures, records, PriorKnowledge.None, cancellationToken);

    /// <summary>
    /// Mentioned entities reach the prompt only. Record linkage compares records of the same kind of
    /// thing, and a mentioning record (a work order naming its machine) is not a record of the
    /// mentioned entity, so the pre-filter does not run against them and a merge candidate's confidence
    /// is the model's own. Where records do not each denote one entity (chunks of a document), no entity
    /// has a denoting record, so no merge candidate survives — by design, as no known-entity identity does.
    /// Known types reach the prompt, and the parse: a proposed type that is a spelling of a name in use
    /// (<see cref="TypeNames.SameType"/>) is written as that name, so <c>WorkOrder</c> answered for
    /// <c>work_order</c> does not become a second type; a near misspelling of one name in use is written as
    /// that name only when the records the answer cited carry its words (<see cref="OntologyProposal.RespelledTypeNames"/>).
    /// Whether a kind is the one a name stands for stays the model's judgment; only the spelling is settled deterministically.
    /// </summary>
    public async Task<OntologyProposal> ProposeAsync(IReadOnlyList<DeclaredStructure> declaredStructures, IReadOnlyList<RawRecord> records, PriorKnowledge prior, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prior);
        DeclaredStructureMerge.EnsureDistinctSubjects(declaredStructures);
        DeclaredStructureMerge.EnsureValidSuperTypes(declaredStructures);
        prior.Validate(records);
        var typeRelations = proposeTypeRelations || declaredStructures.Any(d => d.SuperTypes.Count > 0);
        var knownEntities = prior.KnownEntities;
        var mentionedEntities = prior.MentionedEntities;
        var identity = LinkageEvidence.IdentityView(records, declaredStructures);
        var scope = LinkageEvidence.OwnershipScope(records, declaredStructures);
        var linkageAnalysis = linkageParameters is null
            ? LinkagePipeline.Analyze(identity, knownEntities, options, scope)
            : LinkagePipeline.Analyze(identity, knownEntities, linkageParameters, options, scope);
        var promptedPairs = SelectPromptedPairs(linkageAnalysis.Clustering.GrayZonePairs, options.MaxGrayZonePairsInPrompt);
        var prompt = BuildPrompt(declaredStructures, records, prior, linkageAnalysis, promptedPairs, typeRelations);
        var schema = ResponseSchemaFor(knownEntities.Count > 0, mentionedEntities.Count > 0, typeRelations);
        var response = await modelClient.CompleteAsync(new ModelRequest(prompt, schema), cancellationToken).ConfigureAwait(false);
        var parsed = ParseResponse(response.Text, declaredStructures, records, prior, linkageAnalysis, options.RecordsDenoteEntities, typeRelations);
        var proposal = DeclaredStructureMerge.Apply(declaredStructures, parsed.Proposal.Entities, parsed.Proposal.Relations, parsed.Proposal.Rejections);
        return proposal with
        {
            Linkage = new LinkageReport(
                linkageAnalysis,
                FindSplitClusters(linkageAnalysis.Clustering, proposal.Entities),
                linkageAnalysis.Clustering.GrayZonePairs.Count - promptedPairs.Count),
            MergeCandidates = parsed.MergeCandidates,
            DemotedDenotations = parsed.DemotedDenotations,
            TypeRelations = parsed.TypeRelations,
            RespelledTypeNames = parsed.RespelledTypeNames,
        };
    }

    /// <summary>
    /// The gray-zone pairs the prompt carries: all of them up to <paramref name="cap"/>; past it, the
    /// <paramref name="cap"/> with the strongest prior toward the same entity, kept in their original
    /// order — so a batch under the cap sends exactly the prompt it always sent.
    /// </summary>
    private static IReadOnlyList<PairLinkage> SelectPromptedPairs(IReadOnlyList<PairLinkage> pairs, int cap)
    {
        if (pairs.Count <= cap)
        {
            return pairs;
        }

        return pairs
            .Select((pair, index) => (pair, index))
            .OrderByDescending(p => p.pair.LogLikelihoodRatio)
            .ThenBy(p => p.index)
            .Take(cap)
            .OrderBy(p => p.index)
            .Select(p => p.pair)
            .ToList();
    }

    /// <summary>Confirmed groups whose records the answer attributes to two or more different entities.</summary>
    private static List<RecordCluster> FindSplitClusters(ClusteringResult clustering, IReadOnlyList<EntityProposal> entities)
    {
        var split = new List<RecordCluster>();
        foreach (var cluster in clustering.Clusters.Where(c => c.RecordIds.Count > 1))
        {
            var members = cluster.RecordIds.ToHashSet(StringComparer.Ordinal);
            if (entities.Count(e => e.DenotedBy.Any(members.Contains)) > 1)
            {
                split.Add(cluster);
            }
        }

        return split;
    }

    private static string BuildPrompt(IReadOnlyList<DeclaredStructure> declaredStructures, IReadOnlyList<RawRecord> records, PriorKnowledge prior, LinkageAnalysis linkageAnalysis, IReadOnlyList<PairLinkage> promptedPairs, bool typeRelations)
    {
        var (knownEntities, mentionedEntities, knownTypes) = prior;
        var text = new StringBuilder();
        text.AppendLine(PromptInstruction);
        text.AppendLine(knownEntities.Count > 0 ? PromptSchemaWithKnownEntities : PromptSchema);
        if (mentionedEntities.Count > 0)
        {
            text.AppendLine(PromptSchemaMergeCandidates);
        }

        if (typeRelations)
        {
            text.AppendLine(PromptSchemaTypeRelations);
        }

        text.AppendLine(PromptReferenceRule);

        if (declaredStructures.Count > 0)
        {
            text.AppendLine();
            text.AppendLine(DeclarationClause);
            foreach (var declared in declaredStructures)
            {
                text.AppendLine(DescribeType(declared));
            }

            foreach (var declared in declaredStructures)
            {
                foreach (var relation in declared.Relations)
                {
                    text.AppendLine(CultureInfo.InvariantCulture, $"Declared relation {DeclaredStructureMerge.DescribeRelation(declared, relation)}");
                }
            }

            foreach (var declared in declaredStructures)
            {
                foreach (var superType in declared.SuperTypes)
                {
                    text.AppendLine(CultureInfo.InvariantCulture, $"Declared type relation: {declared.Subject} is a kind of {superType.Trim()}");
                }
            }
        }

        if (knownEntities.Count > 0)
        {
            text.AppendLine();
            text.AppendLine(KnownEntitiesClause);
            foreach (var known in knownEntities)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"Known entity: key={known.Key}, type={known.EntityType}, name={known.Name}");
                foreach (var record in known.DenotingRecords ?? [])
                {
                    text.AppendLine(CultureInfo.InvariantCulture, $"  - {record.Id}: {DescribeFields(record)}");
                }
            }
        }

        if (mentionedEntities.Count > 0)
        {
            text.AppendLine();
            text.AppendLine(MentionedEntitiesClause);
            foreach (var mentioned in mentionedEntities)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"Mentioned entity: key={mentioned.Key}, type={mentioned.EntityType}, name={mentioned.Name}");
                foreach (var record in mentioned.MentioningRecords ?? [])
                {
                    text.AppendLine(CultureInfo.InvariantCulture, $"  - {record.Id}: {DescribeFields(record)}");
                }
            }
        }

        if (knownTypes.Count > 0)
        {
            text.AppendLine();
            text.AppendLine(KnownTypesClause);
            foreach (var known in knownTypes)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"Known type: {known.Name.Trim()}");
                foreach (var record in known.Specimens ?? [])
                {
                    text.AppendLine(CultureInfo.InvariantCulture, $"  - {record.Id}: {DescribeFields(record)}");
                }
            }
        }

        if (typeRelations)
        {
            text.AppendLine();
            text.AppendLine(TypeRelationsClause);
        }

        text.AppendLine();
        text.AppendLine("Records:");
        foreach (var record in records)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"- {record.Id}: {DescribeFields(record)}");
        }

        var linkedClusters = linkageAnalysis.Clustering.Clusters.Where(c => c.RecordIds.Count > 1).ToList();
        if (linkedClusters.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Pre-linked record groups (record linkage already confirmed these denote the same entity -- merge them, do not re-decide):");
            foreach (var cluster in linkedClusters)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- {string.Join(", ", cluster.RecordIds)}");
            }
        }

        var linkedToKnown = linkageAnalysis.KnownCandidates.Where(c => c.Classification == LinkageClassification.Match).ToList();
        if (linkedToKnown.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Records pre-linked to known entities (record linkage already confirmed the record denotes that known entity -- use its key, do not re-decide):");
            foreach (var candidate in linkedToKnown)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- {candidate.RecordId} -> {candidate.KnownEntityKey}");
            }
        }

        var maybeKnown = linkageAnalysis.KnownCandidates.Where(c => c.Classification == LinkageClassification.GrayZone).ToList();
        if (maybeKnown.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Records that may denote a known entity, needing your judgment (prior evidence toward the same entity, in log-odds -- positive favors the same entity):");
            foreach (var candidate in maybeKnown)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- {candidate.RecordId} vs {candidate.KnownEntityKey}: prior log-odds {candidate.LogLikelihoodRatio:F2}");
            }
        }

        if (promptedPairs.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Ambiguous record pairs needing your judgment (prior evidence toward same entity, in log-odds -- positive favors same entity, negative favors different entities):");
            foreach (var pair in promptedPairs)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- {pair.RecordIdA} vs {pair.RecordIdB}: prior log-odds {pair.LogLikelihoodRatio:F2}");
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// "Declared type: work_order (fields: wo_no (text, required), qty (integer))", or just
    /// "Declared type: Organization" when the caller declared the name alone — a type known only by
    /// name is still declared vocabulary the model is to use. A declared business name follows the type
    /// in quotes, as a field's does: "Declared type: inspection_log \"설비 점검일지\" (fields: …)".
    /// </summary>
    private static string DescribeType(DeclaredStructure declared)
    {
        var name = string.IsNullOrWhiteSpace(declared.BusinessName) ? declared.Subject.Value : $"{declared.Subject} \"{declared.BusinessName.Trim()}\"";
        return declared.Fields.Count == 0
            ? $"Declared type: {name}"
            : $"Declared type: {name} (fields: {string.Join(", ", declared.Fields.Select(DescribeField))})";
    }

    /// <summary>
    /// "qty (integer, required; monetary amount)" — every declared fact the caller supplied, so the
    /// model is told what is declared rather than left to re-infer it from a sample. A declared
    /// business name follows the machine name in quotes: "lot_no \"로트 번호\" (text, required)".
    /// </summary>
    private static string DescribeField(DeclaredField field)
    {
        var facts = new List<string>();
        if (field.Kind is { } kind)
        {
            facts.Add(Describe(kind));
        }

        if (field.Required == true)
        {
            facts.Add("required");
        }

        var detail = string.Join(", ", facts);
        if (!string.IsNullOrWhiteSpace(field.SemanticHint))
        {
            detail = detail.Length == 0 ? field.SemanticHint : $"{detail}; {field.SemanticHint}";
        }

        var name = string.IsNullOrWhiteSpace(field.BusinessName) ? field.Name : $"{field.Name} \"{field.BusinessName.Trim()}\"";
        return detail.Length == 0 ? name : $"{name} ({detail})";
    }

    private static string Describe(DeclaredValueKind kind) => kind switch
    {
        DeclaredValueKind.Text => "text",
        DeclaredValueKind.WholeNumber => "integer",
        DeclaredValueKind.FractionalNumber => "decimal",
        DeclaredValueKind.Boolean => "boolean",
        DeclaredValueKind.Timestamp => "timestamp",
        DeclaredValueKind.Identifier => "identifier",
        DeclaredValueKind.Structured => "structured",
        _ => kind.ToString().ToLowerInvariant(),
    };

    private static string DescribeFields(RawRecord record) => string.Join(", ", record.Fields.Select(f => $"{f.Key}={f.Value}"));

    /// <summary>
    /// Turns the model's answer into a proposal in a fixed order, so each later step can rely on what
    /// the earlier ones removed: invalid JSON and invented sources refuse the whole response; then
    /// each entity is checked on its own, then entity ids for duplicates, then each relation on its
    /// own and against the entities that survived. Origin is stamped from
    /// <see cref="InnateVocabulary"/>, not read from the model.
    /// </summary>
    private static ParsedResponse ParseResponse(string responseText, IReadOnlyList<DeclaredStructure> declaredStructures, IReadOnlyList<RawRecord> records, PriorKnowledge prior, LinkageAnalysis linkageAnalysis, bool recordsDenoteEntities, bool typeRelations)
    {
        var (knownEntities, mentionedEntities, _) = prior;
        var knownKeys = knownEntities.Select(k => k.Key).ToHashSet(StringComparer.Ordinal);
        var knownEntityTypes = knownEntities.ToDictionary(k => k.Key, k => k.EntityType, StringComparer.Ordinal);
        var namesInUse = NamesInUse(declaredStructures, prior);
        var recordsById = records.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var respellings = new List<RespelledTypeName>();
        ProposalResponse parsed;
        try
        {
            parsed = JsonSerializer.Deserialize(responseText, ResponseJsonContext.Default.ProposalResponse)
                ?? new ProposalResponse(null, null);
        }
        catch (JsonException ex)
        {
            throw new FormatException(
                $"The model response was not valid JSON. Response text: {Excerpt(responseText)}",
                ex);
        }

        var entityResponses = parsed.Entities ?? [];
        var relationResponses = parsed.Relations ?? [];
        var candidateResponses = parsed.MergeCandidates ?? [];

        // Type relations the call did not ask for are not read at all: the request did not offer the
        // channel, so whatever arrived there is not an answer to it.
        var typeRelationResponses = typeRelations ? parsed.TypeRelations ?? [] : [];
        RejectUnknownSources(entityResponses, relationResponses, candidateResponses, typeRelationResponses, records, responseText);

        var rejections = new List<ProposalRejection>();
        var duplicatedIds = entityResponses
            .Where(e => !string.IsNullOrWhiteSpace(e.Id))
            .GroupBy(e => e.Id!, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);

        // Identity claims made through a declared reference field are withdrawn before anything reads
        // them -- linkage confidence, known-entity matching and merge candidates all weigh "denotedBy".
        var demotions = recordsDenoteEntities
            ? DeclaredReferenceDenotation.Find(
                declaredStructures,
                records,
                entityResponses
                    .Where(e => EntityDefect(e) is null && !duplicatedIds.Contains(e.Id!) && KnownEntityDefect(e, knownKeys) is null)
                    .Select(e => new DeclaredReferenceDenotation.Claim(e.Id!, e.Name!, Denoting(e)))
                    .ToList())
            : [];
        var demotedRecords = demotions
            .GroupBy(d => d.EntityId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(d => d.RecordId).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);

        var entities = new List<EntityProposal>();
        foreach (var e in entityResponses)
        {
            var defect = EntityDefect(e) ?? (duplicatedIds.Contains(e.Id!)
                ? new Defect(RejectionReason.DuplicateEntityId, "more than one entity was proposed under this id — an entity id must name one entity")
                : null) ?? KnownEntityDefect(e, knownKeys);
            if (defect is not null)
            {
                rejections.Add(new ProposalRejection(ProposalElement.Entity, BlankToNull(e.Id), defect.Reason, $"entity {Describe(e.Id)}: {defect.Detail}"));
                continue;
            }

            // Linkage evidence bears on whether records are the same entity, so it adjusts only the
            // records claimed to denote this one. A record that merely mentions the entity -- a work
            // order naming its machine -- says nothing about whether it is the same thing as another,
            // and reading it that way penalized exactly the entities many rows refer to.
            // Where records do not each denote one entity -- chunks of a document -- no record is an
            // entity, whatever the answer says: a record it names there is kept as a citation, never as
            // an identity. Models fill the field for chunks anyway, and not the same way twice.
            var denotedBy = recordsDenoteEntities
                ? Denoting(e).Where(id => !demotedRecords.TryGetValue(e.Id!, out var demoted) || !demoted.Contains(id)).ToList()
                : [];
            var claim = ToClaim(e.Claim!, EntitySources(e));
            var knownKey = BlankToNull(e.KnownEntityKey);
            var confidence = LinkageConfidenceAdjuster.AdjustConfidence(denotedBy, e.Confidence!.Value, linkageAnalysis);
            if (knownKey is not null)
            {
                confidence = LinkageConfidenceAdjuster.AdjustForKnownEntity(denotedBy, knownKey, confidence, linkageAnalysis);
            }

            // A known entity wins as a declaration does, its type included: the entity keeps the type it
            // was already exported under, or one thing would be stated as two kinds across calls. Any
            // other type that spells a name in use is written as that name.
            var type = knownKey is not null ? knownEntityTypes[knownKey] : InUseSpelling(e.Type!, namesInUse, namesInUse.Values, EntitySources(e), recordsById, respellings);
            entities.Add(EntityProposal.Create(e.Id!, e.Name!.Trim(), type, claim, InnateVocabulary.OfEntityType(type), confidence, denotedBy: denotedBy, knownEntityKey: knownKey));
        }

        var proposedIds = entityResponses.Where(e => !string.IsNullOrWhiteSpace(e.Id)).Select(e => e.Id!).ToHashSet(StringComparer.Ordinal);
        var survivingIds = entities.Select(e => e.EntityId).ToHashSet(StringComparer.Ordinal);

        var relations = new List<RelationProposal>();
        foreach (var r in relationResponses)
        {
            var defect = RelationDefect(r) ?? EndpointDefect(r, proposedIds, survivingIds);
            if (defect is not null)
            {
                rejections.Add(new ProposalRejection(ProposalElement.Relation, BlankToNull(r.Name), defect.Reason, $"relation {Describe(r.Name)} ({Describe(r.From)} -> {Describe(r.To)}): {defect.Detail}"));
                continue;
            }

            relations.Add(RelationProposal.Create(r.Name!, r.From!, r.To!, ToClaim(r.Claim!, r.Sources!), InnateVocabulary.OfRelationName(r.Name!), r.Confidence!.Value));
        }

        var mergeCandidates = ParseMergeCandidates(candidateResponses, mentionedEntities, entities, proposedIds, rejections);
        var typeRelationProposals = ParseTypeRelations(typeRelationResponses, declaredStructures, namesInUse, entities, recordsById, respellings, rejections);
        return new ParsedResponse(new OntologyProposal(entities, relations, rejections), mergeCandidates, demotions, typeRelationProposals, respellings);
    }

    /// <summary>
    /// Type relations are checked one by one, like relations, but their ends are type names, not entities
    /// of the response: a record may say what a kind of thing is without being a record of one. An end is
    /// written the way the name is already in use — a declared subject, a declared super type, a known type,
    /// or a type an entity of this answer carries — so <c>work order</c> and <c>WorkOrder</c> do not become
    /// two types. An end the answer misspelled is written as the name in use when its cited records settle it
    /// (<see cref="GroundedRespelling"/>) — against the names given to the call, not the answer's own entity
    /// types, which are the model's spellings. A pair given twice is one type relation; the first is kept. Whether the pair was declared
    /// is decided by <see cref="DeclaredStructureMerge.StampTypeRelations"/>.
    /// </summary>
    private static List<TypeRelationProposal> ParseTypeRelations(IReadOnlyList<TypeRelationResponse> responses, IReadOnlyList<DeclaredStructure> declaredStructures, Dictionary<string, string> namesInUse, IReadOnlyList<EntityProposal> entities, IReadOnlyDictionary<string, RawRecord> recordsById, List<RespelledTypeName> respellings, List<ProposalRejection> rejections)
    {
        var spellings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in declaredStructures.Select(d => d.Subject.Value).Concat(declaredStructures.SelectMany(d => d.SuperTypes).Select(t => t.Trim())))
        {
            spellings.TryAdd(TypeNames.Key(name), name);
        }

        foreach (var (key, name) in namesInUse)
        {
            spellings.TryAdd(key, name);
        }

        var given = spellings.Values.ToList();
        foreach (var entity in entities)
        {
            spellings.TryAdd(TypeNames.Key(entity.EntityType), entity.EntityType);
        }

        var seen = new HashSet<(string, string)>();
        var proposals = new List<TypeRelationProposal>();
        foreach (var t in responses)
        {
            var defect = TypeRelationDefect(t);
            if (defect is not null)
            {
                rejections.Add(new ProposalRejection(ProposalElement.TypeRelation, BlankToNull(t.Sub), defect.Reason, $"type relation {Describe(t.Sub)} is-a {Describe(t.Super)}: {defect.Detail}"));
                continue;
            }

            var sub = InUseSpelling(t.Sub!.Trim(), spellings, given, t.Sources!, recordsById, respellings);
            var super = InUseSpelling(t.Super!.Trim(), spellings, given, t.Sources!, recordsById, respellings);

            // Written as names in use, two ends answered apart can name one type — a misspelled end
            // respelled onto the other: reflexive, rejected as an answered one is.
            if (TypeNames.SameType(sub, super))
            {
                rejections.Add(new ProposalRejection(ProposalElement.TypeRelation, BlankToNull(t.Sub), RejectionReason.ReflexiveTypeRelation, $"type relation {Describe(t.Sub)} is-a {Describe(t.Super)}: both ends are the type in use '{sub}' — a type is not a kind of itself"));
                continue;
            }

            if (seen.Add((TypeNames.Key(sub), TypeNames.Key(super))))
            {
                proposals.Add(TypeRelationProposal.Create(sub, super, ToClaim(t.Claim!, t.Sources!), t.Confidence!.Value));
            }
        }

        return DeclaredStructureMerge.StampTypeRelations(declaredStructures, proposals, rejections);
    }

    private static Defect? TypeRelationDefect(TypeRelationResponse t) =>
        MissingFields(("sub", t.Sub), ("super", t.Super), ("claim", t.Claim)) is { } missing ? new Defect(RejectionReason.MissingField, missing)
        : TypeNames.Key(t.Sub!).Length == 0 || TypeNames.Key(t.Super!).Length == 0 ? new Defect(RejectionReason.MissingField, "a type name needs at least one letter or digit")
        : !CitesSources(t.Sources) ? new Defect(RejectionReason.MissingField, "cites no source")
        : TypeNames.SameType(t.Sub!, t.Super!) ? new Defect(RejectionReason.ReflexiveTypeRelation, "both ends name one type — a type is not a kind of itself")
        : ConfidenceDefect(t.Confidence);

    /// <summary>
    /// The type names in use, by spelling key: a declared business name first, standing for its declared
    /// type (the declaration is authoritative, and the model was shown both names), then known types (the
    /// names the caller keeps), then known entities' types. Where two of them spell one name, the first is
    /// the spelling kept — earlier calls may have written one name two ways, and that is no reason to
    /// refuse the call.
    /// </summary>
    private static Dictionary<string, string> NamesInUse(IReadOnlyList<DeclaredStructure> declaredStructures, PriorKnowledge prior)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var declared in declaredStructures.Where(d => !string.IsNullOrWhiteSpace(d.BusinessName)))
        {
            names.TryAdd(TypeNames.Key(declared.BusinessName!.Trim()), declared.Subject.Value);
        }

        foreach (var name in prior.KnownTypes.Select(t => t.Name.Trim()).Concat(prior.KnownEntities.Select(k => k.EntityType.Trim())))
        {
            names.TryAdd(TypeNames.Key(name), name);
        }

        return names;
    }

    /// <summary>
    /// The type as written in the proposal: the name in use it spells, else the name in use its cited
    /// records settle it as (<see cref="GroundedRespelling"/> — recorded in <paramref name="respellings"/>),
    /// else as answered.
    /// </summary>
    private static string InUseSpelling(string type, Dictionary<string, string> spellings, IReadOnlyCollection<string> namesInUse, IEnumerable<string?> sources, IReadOnlyDictionary<string, RawRecord> recordsById, List<RespelledTypeName> respellings)
    {
        if (spellings.TryGetValue(TypeNames.Key(type), out var inUse))
        {
            return inUse;
        }

        var cited = sources.OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (GroundedRespelling.Find(type, namesInUse, GroundedRespelling.CitedWords(cited, recordsById)) is not { } respelled)
        {
            return type;
        }

        if (!respellings.Any(r => r.Answered == type && r.InUse == respelled))
        {
            respellings.Add(new RespelledTypeName(type, respelled, cited));
        }

        return respelled;
    }

    private sealed record ParsedResponse(OntologyProposal Proposal, IReadOnlyList<MergeCandidate> MergeCandidates, IReadOnlyList<DemotedDenotation> DemotedDenotations, IReadOnlyList<TypeRelationProposal> TypeRelations, IReadOnlyList<RespelledTypeName> RespelledTypeNames);

    /// <summary>
    /// Merge candidates are checked after the entities, against the ones that survived and the records
    /// they were left to denote: a candidate's evidence is a record of the thing itself, so an entity
    /// with none — whether the answer gave none, or records here do not each denote one entity — has
    /// nothing to put against a mention, and the candidate is left out. A pair given twice is one
    /// candidate; the first is kept.
    /// </summary>
    private static List<MergeCandidate> ParseMergeCandidates(IReadOnlyList<MergeCandidateResponse> responses, IReadOnlyList<MentionedEntity> mentionedEntities, IReadOnlyList<EntityProposal> entities, HashSet<string> proposedIds, List<ProposalRejection> rejections)
    {
        var mentionedKeys = mentionedEntities.Select(m => m.Key).ToHashSet(StringComparer.Ordinal);
        var survivingById = entities.ToDictionary(e => e.EntityId, StringComparer.Ordinal);
        var seen = new HashSet<(string, string)>();
        var candidates = new List<MergeCandidate>();
        foreach (var c in responses)
        {
            var defect = CandidateDefect(c) ?? MentionedKeyDefect(c, mentionedKeys) ?? CandidateEntityDefect(c, proposedIds, survivingById);
            if (defect is not null)
            {
                rejections.Add(new ProposalRejection(ProposalElement.MergeCandidate, BlankToNull(c.Entity), defect.Reason, $"merge candidate {Describe(c.Entity)} ~ {Describe(c.MentionedKey)}: {defect.Detail}"));
                continue;
            }

            if (seen.Add((c.Entity!, c.MentionedKey!)))
            {
                candidates.Add(MergeCandidate.Create(c.Entity!, c.MentionedKey!, ToClaim(c.Claim!, c.Sources!), c.Confidence!.Value));
            }
        }

        return candidates;
    }

    private static Defect? CandidateDefect(MergeCandidateResponse c) =>
        MissingFields(("entity", c.Entity), ("mentionedKey", c.MentionedKey), ("claim", c.Claim)) is { } missing ? new Defect(RejectionReason.MissingField, missing)
        : !CitesSources(c.Sources) ? new Defect(RejectionReason.MissingField, "cites no source")
        : ConfidenceDefect(c.Confidence);

    private static Defect? MentionedKeyDefect(MergeCandidateResponse c, HashSet<string> mentionedKeys) =>
        mentionedKeys.Contains(c.MentionedKey!) ? null
        : new Defect(RejectionReason.UnknownMentionedEntity, mentionedKeys.Count == 0
            ? $"names mentioned entity '{c.MentionedKey}', but the call supplied no mentioned entities"
            : $"names mentioned entity '{c.MentionedKey}', which is not one of the {mentionedKeys.Count} the call supplied");

    private static Defect? CandidateEntityDefect(MergeCandidateResponse c, HashSet<string> proposedIds, Dictionary<string, EntityProposal> survivingById) =>
        !survivingById.TryGetValue(c.Entity!, out var entity)
            ? new Defect(RejectionReason.CandidateEntityUnresolved, proposedIds.Contains(c.Entity!)
                ? $"{c.Entity} was proposed but rejected"
                : $"{c.Entity} names no entity the response proposed")
            : entity.DenotedBy.Count == 0
                ? new Defect(RejectionReason.CandidateEntityNotDenoted, $"{c.Entity} is denoted by no record of this call, so nothing here is evidence of what it is")
                : null;

    private sealed record Defect(RejectionReason Reason, string Detail);

    private static Defect? EntityDefect(EntityResponse e) =>
        MissingFields(("id", e.Id), ("name", e.Name), ("type", e.Type), ("claim", e.Claim)) is { } missing ? new Defect(RejectionReason.MissingField, missing)
        : !CitesSources(EntitySources(e)) ? new Defect(RejectionReason.MissingField, "cites no source")
        : ConfidenceDefect(e.Confidence);

    /// <summary>
    /// A match to a known entity names one the call supplied. A key the call never gave matches nothing
    /// the caller can join, so the entity is left out — reported like any other defective element, not
    /// a reason to distrust the rest of the answer, because the key is not evidence the way a cited
    /// record is.
    /// </summary>
    private static Defect? KnownEntityDefect(EntityResponse e, HashSet<string> knownKeys) =>
        BlankToNull(e.KnownEntityKey) is { } key && !knownKeys.Contains(key)
            ? new Defect(RejectionReason.UnknownKnownEntity, knownKeys.Count == 0
                ? $"matched to known entity '{key}', but the call supplied no known entities"
                : $"matched to known entity '{key}', which is not one of the {knownKeys.Count} the call supplied")
            : null;

    private static Defect? RelationDefect(RelationResponse r) =>
        MissingFields(("name", r.Name), ("from", r.From), ("to", r.To), ("claim", r.Claim)) is { } missing ? new Defect(RejectionReason.MissingField, missing)
        : !CitesSources(r.Sources) ? new Defect(RejectionReason.MissingField, "cites no source")
        : ConfidenceDefect(r.Confidence);

    /// <summary>
    /// An entity's evidence: its sources, and any record it names as denoting it that the sources
    /// left out — a record that is the entity is evidence for it whether or not the model repeated
    /// it, so it is added rather than the entity refused.
    /// </summary>
    private static List<string?> EntitySources(EntityResponse e) =>
        (e.Sources ?? []).Concat(Denoting(e).Where(id => !(e.Sources ?? []).Contains(id))).ToList();

    /// <summary>
    /// The records claimed to denote the entity. An answer that omits the field makes no identity
    /// claim, so nothing is linked — the schema requires the field, and a record is not taken to
    /// denote an entity because it was cited as mentioning it.
    /// </summary>
    private static List<string> Denoting(EntityResponse e) =>
        (e.DenotedBy ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id!).Distinct(StringComparer.Ordinal).ToList();

    private static bool CitesSources(IReadOnlyList<string?>? sources) =>
        sources is { Count: > 0 } && sources.All(id => !string.IsNullOrWhiteSpace(id));

    private static string? MissingFields(params (string Field, string? Value)[] fields)
    {
        var missing = fields.Where(f => string.IsNullOrWhiteSpace(f.Value)).Select(f => $"\"{f.Field}\"").ToList();
        return missing.Count == 0 ? null : $"missing {string.Join(", ", missing)}";
    }

    private static Defect? ConfidenceDefect(double? confidence) => confidence switch
    {
        null => new Defect(RejectionReason.MissingField, "missing \"confidence\""),
        < 0.0 or > 1.0 => new Defect(RejectionReason.ConfidenceOutOfRange, $"confidence {confidence.Value.ToString(CultureInfo.InvariantCulture)} is outside [0, 1]"),
        _ => null,
    };

    /// <summary>
    /// A relation's two ends are entities of the same response — that is what makes it a relation
    /// rather than a name with two strings attached. An end that is not a surviving entity cannot be
    /// resolved by anyone downstream, so the relation is left out. The two ways that happens are kept
    /// apart because they are different findings: an id the response never proposed (often a value
    /// the model referred to without standing it up as an entity), and an entity that was proposed
    /// but itself rejected.
    /// </summary>
    private static Defect? EndpointDefect(RelationResponse r, HashSet<string> proposedIds, HashSet<string> survivingIds)
    {
        var unresolved = new[] { r.From!, r.To! }.Distinct(StringComparer.Ordinal).Where(id => !survivingIds.Contains(id)).ToList();
        if (unresolved.Count == 0)
        {
            return null;
        }

        var dangling = unresolved.Where(id => !proposedIds.Contains(id)).ToList();
        return dangling.Count > 0
            ? new Defect(RejectionReason.DanglingRelationEnd, $"{string.Join(", ", dangling)} names no entity the response proposed")
            : new Defect(RejectionReason.EndpointRejected, $"{string.Join(", ", unresolved)} was proposed but rejected");
    }

    private static string Describe(string? value) => string.IsNullOrWhiteSpace(value) ? "(unnamed)" : value;

    private static string? BlankToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// The only sources a claim can cite are the records this call was given. A model that cites an
    /// id it was never shown has produced the <em>shape</em> of grounding with none of the substance,
    /// and the set of ids is already in hand — so this is a deterministic check, not a judgment.
    /// Unlike every other defect, this one refuses the whole response rather than the offending
    /// element: the check can see that a cited id exists, never that the record says what the claim
    /// says, so a response that invented evidence once gives no ground for trusting its other
    /// citations.
    /// </summary>
    private static void RejectUnknownSources(IReadOnlyList<EntityResponse> entities, IReadOnlyList<RelationResponse> relations, IReadOnlyList<MergeCandidateResponse> candidates, IReadOnlyList<TypeRelationResponse> typeRelations, IReadOnlyList<RawRecord> records, string responseText)
    {
        var known = records.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var cited = entities.SelectMany(EntitySources)
            .Concat(relations.SelectMany(r => r.Sources ?? []))
            .Concat(candidates.SelectMany(c => c.Sources ?? []))
            .Concat(typeRelations.SelectMany(t => t.Sources ?? []))
            .OfType<string>()
            .Where(id => !string.IsNullOrWhiteSpace(id));
        var unknown = cited.Where(id => !known.Contains(id)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (unknown.Count == 0)
        {
            return;
        }

        var reason = known.Count == 0
            ? "no records were supplied to this call, so no source can be cited"
            : $"the call supplied {known.Count} record(s) and none of these is among them";
        throw new FormatException(
            $"The model cited source id(s) it was never given: {string.Join(", ", unknown)} — {reason}. Response text: {Excerpt(responseText)}");
    }

    private static GroundedClaim ToClaim(string claim, IReadOnlyList<string?> sources) =>
        GroundedClaim.Create(claim, sources.Select(id => new SourceRef(id!)).ToList());

    /// <summary>
    /// Bounded excerpt of what the model actually returned. A model that wraps its JSON in prose
    /// or a markdown fence fails here, and without the text the caller cannot tell that apart from
    /// a truncated or refused completion — the text is already in hand, so the exception carries
    /// it rather than discarding it.
    /// </summary>
    private static string Excerpt(string text) => text.Length switch
    {
        0 => "(empty)",
        <= DiagnosticExcerptLength => text,
        _ => $"{text[..DiagnosticExcerptLength]}… ({text.Length} chars total)",
    };

    private sealed record ProposalResponse(IReadOnlyList<EntityResponse>? Entities, IReadOnlyList<RelationResponse>? Relations, IReadOnlyList<MergeCandidateResponse>? MergeCandidates = null, IReadOnlyList<TypeRelationResponse>? TypeRelations = null);

    private sealed record TypeRelationResponse(string? Sub, string? Super, string? Claim, IReadOnlyList<string?>? Sources, double? Confidence);

    private sealed record MergeCandidateResponse(string? Entity, string? MentionedKey, string? Claim, IReadOnlyList<string?>? Sources, double? Confidence);

    // Every field is nullable: a model can omit any of them, and an omission is a per-element
    // rejection, not a deserialization failure of the whole answer.
    private sealed record EntityResponse(string? Id, string? Name, string? Type, string? Claim, IReadOnlyList<string?>? Sources, IReadOnlyList<string?>? DenotedBy, double? Confidence, string? KnownEntityKey = null);

    private sealed record RelationResponse(string? Name, string? From, string? To, string? Claim, IReadOnlyList<string?>? Sources, double? Confidence);

    /// <summary>
    /// The response shape's serialization metadata, generated at compile time so the parse works
    /// where reflection-based serialization is off (Native AOT, trimmed hosts).
    /// </summary>
    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
    [JsonSerializable(typeof(ProposalResponse))]
    private sealed partial class ResponseJsonContext : JsonSerializerContext;
}
