using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
/// </summary>
public sealed class SinglePassOntologyProposer(IModelClient modelClient, LinkageOptions? linkageOptions = null) : IOntologyProposer
{
    private const int DiagnosticExcerptLength = 500;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly LinkageOptions options = linkageOptions ?? LinkageOptions.Default;

    // The prompt's fixed text — the part that is identical across every call, regardless of the
    // declared structure or records handed in. BuildPrompt appends these verbatim; PromptFingerprint
    // hashes them. Keeping the literals here (rather than inline in BuildPrompt) makes them the
    // single source both use, so the fingerprint cannot silently disagree with the prompt.
    private const string PromptInstruction = "Propose entities and relations grounded in the input below.";
    private const string PromptSchema = "Respond with JSON only: {\"entities\":[{\"id\",\"name\",\"type\",\"claim\",\"sources\",\"denotedBy\",\"confidence\"}],\"relations\":[{\"name\",\"from\",\"to\",\"claim\",\"sources\",\"confidence\"}]}.";
    private const string PromptReferenceRule = "An entity's \"id\" only links relations to it within this response; its \"name\" is the entity as the records write it. Every claim must cite at least one source id. In an entity's \"denotedBy\", list the source ids that are records of that entity itself, not those that only refer to it.";

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

    // Two wordings of the floor sentence failed in measurement, each in one regime. With only
    // "beyond what is declared" after the request to use declared types, the model kept document
    // chunks to exactly the declared entity types. Saying the declared types are not the only ones
    // restored that, but adding "wherever nothing declared describes it" closed the form regime
    // instead: a record the declared type already describes left nothing to propose beyond it. The
    // sentence keeps both halves that worked and names no kind of entity (that would be the
    // steering design rationale §E keeps out of the prompt).
    private const string DeclarationClause = "Declared structure is authoritative: a declared type, field or relation is fact, not a hypothesis. Where a declared type describes an entity, propose the entity under that type; where a declared relation describes a relation, propose it under that name; never contradict declared structure. A declaration is a floor, not a ceiling: the declared types and relations are not the only ones, so still propose every entity and relation the records show beyond what is declared, under types and names of your own.";

    /// <summary>
    /// First 8 hex characters of the SHA-256 of the request's fixed part (preamble + the declaration
    /// clause + the response schema) — a short, stable identifier that changes whenever that wording
    /// or schema changes and stays the same otherwise. A measurement report stamps it so two runs
    /// made across a prompt edit are not read as comparable by accident. It covers only the fixed
    /// part, not the per-call declared structure or records, so the same prompt version yields the
    /// same fingerprint on any input.
    /// </summary>
    public static string PromptFingerprint { get; } = ComputeFingerprint();

    private static string ComputeFingerprint()
    {
        var fixedText = string.Join('\n', PromptInstruction, PromptSchema, PromptReferenceRule, DeclarationClause, ResponseSchema.GetRawText());
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(fixedText));
        return Convert.ToHexString(hash).ToLowerInvariant()[..8];
    }

    public async Task<OntologyProposal> ProposeAsync(IReadOnlyList<DeclaredStructure> declaredStructures, IReadOnlyList<RawRecord> records, CancellationToken cancellationToken = default)
    {
        DeclaredStructureMerge.EnsureDistinctSubjects(declaredStructures);
        var linkageAnalysis = LinkagePipeline.Analyze(records, options);
        var prompt = BuildPrompt(declaredStructures, records, linkageAnalysis);
        var response = await modelClient.CompleteAsync(new ModelRequest(prompt, ResponseSchema), cancellationToken).ConfigureAwait(false);
        var parsed = ParseResponse(response.Text, records, linkageAnalysis, options.RecordsDenoteEntities);
        return DeclaredStructureMerge.Apply(declaredStructures, parsed.Entities, parsed.Relations, parsed.Rejections);
    }

    private static string BuildPrompt(IReadOnlyList<DeclaredStructure> declaredStructures, IReadOnlyList<RawRecord> records, LinkageAnalysis linkageAnalysis)
    {
        var text = new StringBuilder();
        text.AppendLine(PromptInstruction);
        text.AppendLine(PromptSchema);
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
                    text.AppendLine(CultureInfo.InvariantCulture, $"Declared relation: {DescribeRelation(declared, relation)}");
                }
            }
        }

        text.AppendLine();
        text.AppendLine("Records:");
        foreach (var record in records)
        {
            var fields = string.Join(", ", record.Fields.Select(f => $"{f.Key}={f.Value}"));
            text.AppendLine(CultureInfo.InvariantCulture, $"- {record.Id}: {fields}");
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

        if (linkageAnalysis.Clustering.GrayZonePairs.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Ambiguous record pairs needing your judgment (prior evidence toward same entity, in log-odds -- positive favors same entity, negative favors different entities):");
            foreach (var pair in linkageAnalysis.Clustering.GrayZonePairs)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- {pair.RecordIdA} vs {pair.RecordIdB}: prior log-odds {pair.LogLikelihoodRatio:F2}");
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// "Declared type: work_order (fields: wo_no (text, required), qty (integer))", or just
    /// "Declared type: Organization" when the caller declared the name alone — a type known only by
    /// name is still declared vocabulary the model is to use.
    /// </summary>
    private static string DescribeType(DeclaredStructure declared)
        => declared.Fields.Count == 0
            ? $"Declared type: {declared.Subject}"
            : $"Declared type: {declared.Subject} (fields: {string.Join(", ", declared.Fields.Select(DescribeField))})";

    /// <summary>
    /// "qty (integer, required; monetary amount)" — every declared fact the caller supplied, so the
    /// model is told what is declared rather than left to re-infer it from a sample.
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

        return detail.Length == 0 ? field.Name : $"{field.Name} ({detail})";
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

    /// <summary>
    /// "asset: work_order -> asset (reference via asset_tag)" — both ends, since with several
    /// declared subjects the relation's name alone no longer says which type it leaves; and which
    /// field carries it, and on which side.
    /// </summary>
    private static string DescribeRelation(DeclaredStructure declared, DeclaredRelation relation)
    {
        var facts = new List<string>();
        if (relation.Kind is { } kind)
        {
            facts.Add(kind.ToString().ToLowerInvariant());
        }

        if (!string.IsNullOrWhiteSpace(relation.ViaField))
        {
            facts.Add($"via {relation.ViaField}");
        }

        var head = $"{relation.Name}: {declared.Subject} -> {relation.Target}";
        return facts.Count == 0 ? head : $"{head} ({string.Join(" ", facts)})";
    }

    /// <summary>
    /// Turns the model's answer into a proposal in a fixed order, so each later step can rely on what
    /// the earlier ones removed: invalid JSON and invented sources refuse the whole response; then
    /// each entity is checked on its own, then entity ids for duplicates, then each relation on its
    /// own and against the entities that survived. Origin is stamped from
    /// <see cref="InnateVocabulary"/>, not read from the model.
    /// </summary>
    private static OntologyProposal ParseResponse(string responseText, IReadOnlyList<RawRecord> records, LinkageAnalysis linkageAnalysis, bool recordsDenoteEntities)
    {
        ProposalResponse parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ProposalResponse>(responseText, JsonOptions)
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
        RejectUnknownSources(entityResponses, relationResponses, records, responseText);

        var rejections = new List<ProposalRejection>();
        var duplicatedIds = entityResponses
            .Where(e => !string.IsNullOrWhiteSpace(e.Id))
            .GroupBy(e => e.Id!, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);

        var entities = new List<EntityProposal>();
        foreach (var e in entityResponses)
        {
            var defect = EntityDefect(e) ?? (duplicatedIds.Contains(e.Id!)
                ? new Defect(RejectionReason.DuplicateEntityId, "more than one entity was proposed under this id — an entity id must name one entity")
                : null);
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
            var denotedBy = recordsDenoteEntities ? Denoting(e) : [];
            var claim = ToClaim(e.Claim!, EntitySources(e));
            var confidence = LinkageConfidenceAdjuster.AdjustConfidence(denotedBy, e.Confidence!.Value, linkageAnalysis);
            entities.Add(EntityProposal.Create(e.Id!, e.Name!.Trim(), e.Type!, claim, InnateVocabulary.OfEntityType(e.Type!), confidence, denotedBy: denotedBy));
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

        return new OntologyProposal(entities, relations, rejections);
    }

    private sealed record Defect(RejectionReason Reason, string Detail);

    private static Defect? EntityDefect(EntityResponse e) =>
        MissingFields(("id", e.Id), ("name", e.Name), ("type", e.Type), ("claim", e.Claim)) is { } missing ? new Defect(RejectionReason.MissingField, missing)
        : !CitesSources(EntitySources(e)) ? new Defect(RejectionReason.MissingField, "cites no source")
        : ConfidenceDefect(e.Confidence);

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
    private static void RejectUnknownSources(IReadOnlyList<EntityResponse> entities, IReadOnlyList<RelationResponse> relations, IReadOnlyList<RawRecord> records, string responseText)
    {
        var known = records.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var cited = entities.SelectMany(EntitySources)
            .Concat(relations.SelectMany(r => r.Sources ?? []))
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

    private sealed record ProposalResponse(IReadOnlyList<EntityResponse>? Entities, IReadOnlyList<RelationResponse>? Relations);

    // Every field is nullable: a model can omit any of them, and an omission is a per-element
    // rejection, not a deserialization failure of the whole answer.
    private sealed record EntityResponse(string? Id, string? Name, string? Type, string? Claim, IReadOnlyList<string?>? Sources, IReadOnlyList<string?>? DenotedBy, double? Confidence);

    private sealed record RelationResponse(string? Name, string? From, string? To, string? Claim, IReadOnlyList<string?>? Sources, double? Confidence);
}
