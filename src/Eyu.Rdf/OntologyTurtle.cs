using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Eyu.Core.Grounding;
using Eyu.Core.Primitives;
using Eyu.Core.Proposals;

namespace Eyu.Rdf;

/// <summary>
/// Writes an <see cref="OntologyProposal"/> as an OWL ontology in RDF 1.1 Turtle, so what Eyu proposes
/// can be read by the tools a knowledge-graph practice already runs — an OWL editor, a triple store, a
/// SHACL validator — rather than only by code that knows Eyu's record types.
/// <para>
/// A proposal is instance-level: entities and the relations between them. The ontology is derived
/// from it rather than stated beside it — every distinct entity type becomes an <c>owl:Class</c>, every
/// distinct relation name an <c>owl:ObjectProperty</c>, every entity an <c>owl:NamedIndividual</c> of
/// its class, and every relation an assertion between two individuals. Names are grouped the way Eyu
/// compares them, case, separators and Unicode form ignored, so <c>WorkOrder</c> and <c>work_order</c>
/// are one class, <c>:Workorder</c>, labelled with the first spelling seen. No domain or range is asserted: the proposal shows which
/// types a relation was seen between, which is evidence, not the axiom a range would state.
/// </para>
/// <para>
/// What the proposal knows about each element travels with it, as annotations in
/// <see cref="EyuVocabulary"/>: the claim, each cited record (and field), the confidence and whether
/// the type was declared. A relation's annotations are OWL axiom annotations: an <c>owl:Axiom</c> whose
/// <c>owl:annotatedSource</c>, <c>owl:annotatedProperty</c> and <c>owl:annotatedTarget</c> name the
/// assertion. That is the one form an OWL reader attaches to the assertion itself — RDF reification
/// (<c>rdf:Statement</c>) is outside OWL 2 DL, and an OWL parser drops its links, leaving the
/// annotations on a node that points at nothing. Innate types and relations are
/// written as Eyu's own terms and acquired ones under the caller's <see cref="RdfExportOptions.BaseIri"/>
/// — see <see cref="EyuVocabulary"/> for why neither is aligned to an outside vocabulary.
/// </para>
/// <para>
/// <see cref="OntologyProposal.Rejections"/> is not written: a rejection is a fact about the model's
/// answer, not about the domain, and an ontology that carried it would assert what the proposer
/// refused to. A caller keeps it from the proposal itself.
/// </para>
/// <para>
/// An individual's IRI is derived from what the proposal says the entity is, never from
/// <see cref="EntityProposal.EntityId"/>, which the model picks afresh on every call — so two exports
/// of the same records name one entity alike, and a triple store merging them merges its individuals.
/// See <see cref="IndividualIris"/> for the rule and what it cannot tell apart.
/// </para>
/// <para>
/// The ontology names the rule its IRIs were minted under (<see cref="EyuVocabulary.IriRule"/>), so a
/// store holding exports from releases that minted differently can tell which triples came from which.
/// </para>
/// </summary>
public static class OntologyTurtle
{
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private const string Rdfs = "http://www.w3.org/2000/01/rdf-schema#";
    private const string Owl = "http://www.w3.org/2002/07/owl#";
    private const string Xsd = "http://www.w3.org/2001/XMLSchema#";

    /// <summary>
    /// The value of <see cref="EyuVocabulary.IriRule"/>: the release that introduced the rules this
    /// writer mints class, property and individual IRIs under. Change it in the release that changes
    /// any of them.
    /// </summary>
    private const string IriRule = "0.5.0";

    /// <summary>The proposal as a Turtle document.</summary>
    public static string ToTurtle(OntologyProposal proposal, RdfExportOptions options)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Write(proposal, options, writer);
        return writer.ToString();
    }

    /// <summary>Writes the proposal as a Turtle document to <paramref name="writer"/>.</summary>
    public static void Write(OntologyProposal proposal, RdfExportOptions options, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(writer);

        var baseIri = options.BaseIri.AbsoluteUri;
        var terms = new TermSet(baseIri);
        var individuals = IndividualIris(proposal, options);

        writer.Write("@prefix rdf: <" + Rdf + "> .\n");
        writer.Write("@prefix rdfs: <" + Rdfs + "> .\n");
        writer.Write("@prefix owl: <" + Owl + "> .\n");
        writer.Write("@prefix xsd: <" + Xsd + "> .\n");
        writer.Write("@prefix eyu: <" + EyuVocabulary.Namespace + "> .\n");
        writer.Write("@prefix : <" + baseIri + "> .\n\n");

        writer.Write(Iri(baseIri.TrimEnd('#')) + " a owl:Ontology ;\n");
        writer.Write("    eyu:iriRule " + Literal(IriRule) + " .\n\n");

        foreach (var property in new[] { "iriRule", "claim", "cites", "recordId", "fieldName", "denotedBy", "confidence", "basis", "origin" })
        {
            writer.Write("eyu:" + property + " a owl:AnnotationProperty .\n");
        }

        writer.Write('\n');

        var classes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entity in proposal.Entities)
        {
            if (terms.TryAdd(classes, entity.EntityType, entity.Origin, isClass: true, out var iri))
            {
                WriteTerm(writer, iri, "owl:Class", entity.EntityType, entity.Origin);
            }
        }

        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var relation in proposal.Relations)
        {
            if (terms.TryAdd(properties, relation.RelationName, relation.Origin, isClass: false, out var iri))
            {
                WriteTerm(writer, iri, "owl:ObjectProperty", relation.RelationName, relation.Origin);
            }
        }

        foreach (var entity in proposal.Entities)
        {
            var subject = Iri(individuals[entity.EntityId]);
            writer.Write(subject + " a owl:NamedIndividual, " + classes[Normalize(entity.EntityType)] + " ;\n");
            writer.Write("    rdfs:label " + Text(entity.Name) + " ;\n");
            foreach (var recordId in entity.DenotedBy)
            {
                writer.Write("    eyu:denotedBy " + Literal(recordId) + " ;\n");
            }

            WriteProvenance(writer, entity.Claim, entity.Confidence, entity.Basis);
        }

        foreach (var relation in proposal.Relations)
        {
            var from = Iri(individuals[relation.FromEntityId]);
            var to = Iri(individuals[relation.ToEntityId]);
            var predicate = properties[Normalize(relation.RelationName)];

            writer.Write(from + " " + predicate + " " + to + " .\n");
            writer.Write("[] a owl:Axiom ;\n");
            writer.Write("    owl:annotatedSource " + from + " ;\n");
            writer.Write("    owl:annotatedProperty " + predicate + " ;\n");
            writer.Write("    owl:annotatedTarget " + to + " ;\n");
            WriteProvenance(writer, relation.Claim, relation.Confidence, relation.Basis);
        }
    }

    /// <summary>
    /// The IRI each entity of <paramref name="proposal"/> is written under, by
    /// <see cref="EntityProposal.EntityId"/> — the IRIs <see cref="ToTurtle"/> writes, for a caller that
    /// links its own triples to the exported individuals or joins two exports. Strings, not
    /// <see cref="Uri"/>: under a <c>#</c> namespace an individual is a fragment, and <see cref="Uri"/>
    /// equality ignores fragments.
    /// <para>
    /// An individual is identified by its name and type, compared the way Eyu compares names — case and
    /// separators ignored — and by the records that denote it (<see cref="EntityProposal.DenotedBy"/>),
    /// when any does. The records alone do not identify it: a model reading one row that reports an event
    /// names the aircraft, the part and the event, and says the row denotes each of them, so one set of
    /// denoting records is often claimed by several entities of one proposal. A document chunk denotes
    /// nothing it describes, and there the name and type are the whole key. The key is hashed into
    /// <c>entity/</c> under <see cref="RdfExportOptions.BaseIri"/>, so an IRI is the same length however
    /// many records denote the entity, and reads nothing into their ids.
    /// </para>
    /// <para>
    /// The rule is only as stable as its inputs. Proposed again, an entity keeps its IRI when the model
    /// gives it the same name, type and denoting records — not when it renames the entity, spells its type
    /// differently (a declared vocabulary is what holds the type still) or names a different set of
    /// records. Two different things with one name and one type, denoted by the same records or by none,
    /// get one IRI: nothing in the proposal tells them apart.
    /// </para>
    /// <para>
    /// Within one proposal the same holds: entities with one key are one individual — a model reading a
    /// document chunk by chunk proposes one company once per chunk it appears in — written once for each
    /// entity, so the individual carries every claim, cited record and confidence, as it would after two
    /// exports were merged. A relation end no entity carries, and an entity with no denoting record and no
    /// letter or digit in its name, have nothing to key on: each is written under <c>entity/local/</c> and
    /// its <see cref="EntityProposal.EntityId"/>, an IRI that, like the id, means nothing outside this
    /// proposal.
    /// </para>
    /// </summary>
    public static IReadOnlyDictionary<string, string> IndividualIris(OntologyProposal proposal, RdfExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(options);

        var baseIri = options.BaseIri.AbsoluteUri;
        var keys = proposal.Entities
            .GroupBy(e => e.EntityId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => IdentityKey(g.First()), StringComparer.Ordinal);

        // A relation end no entity carries has nothing to key on either: it keeps a proposal-local IRI.
        foreach (var end in proposal.Relations.SelectMany(r => new[] { r.FromEntityId, r.ToEntityId }))
        {
            keys.TryAdd(end, null);
        }

        return keys.ToDictionary(
            kv => kv.Key,
            kv => kv.Value is { } key
                ? baseIri + "entity/" + Hash(key)
                : baseIri + "entity/local/" + TermSet.LocalName(kv.Key),
            StringComparer.Ordinal);
    }

    // Each part is length-prefixed, so no two different inputs spell the same key whatever characters a
    // record id or name holds. Null when there is nothing to key on: no denoting record, and a name with
    // no letter or digit.
    private static string? IdentityKey(EntityProposal entity)
    {
        var name = Normalize(entity.Name);
        if (entity.DenotedBy.Count == 0 && name.Length == 0)
        {
            return null;
        }

        return Part(Normalize(entity.EntityType)) + Part(name) + string.Concat(entity.DenotedBy
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(Part));

        static string Part(string value) => "|" + value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
    }

    // 128 bits of SHA-256, lowercase hex: long enough that two keys meeting is not a case to handle.
    private static string Hash(string key)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 16));

    private static void WriteTerm(TextWriter writer, string iri, string kind, string label, VocabularyOrigin origin)
    {
        writer.Write(iri + " a " + kind + " ;\n");
        writer.Write("    rdfs:label " + Text(label) + " ;\n");
        writer.Write("    eyu:origin " + Literal(origin == VocabularyOrigin.Innate ? "innate" : "acquired") + " .\n\n");
    }

    private static void WriteProvenance(TextWriter writer, GroundedClaim claim, double confidence, ProposalBasis basis)
    {
        writer.Write("    eyu:claim " + Text(claim.Claim) + " ;\n");
        foreach (var source in claim.Sources)
        {
            writer.Write("    eyu:cites [ eyu:recordId " + Literal(source.RecordId));
            if (source.FieldName is not null)
            {
                writer.Write(" ; eyu:fieldName " + Literal(source.FieldName));
            }

            writer.Write(" ] ;\n");
        }

        writer.Write("    eyu:confidence \"" + ((decimal)confidence).ToString(CultureInfo.InvariantCulture) + "\"^^xsd:decimal ;\n");
        writer.Write("    eyu:basis " + Literal(basis == ProposalBasis.Declared ? "declared" : "inferred") + " .\n\n");
    }

    /// <summary>
    /// Eyu's lexical name comparison — case, separators and Unicode form ignored. The same source file
    /// Eyu.Core compiles (linked, see the project file), so the two packages cannot drift apart on it
    /// and the core carries no public surface that exists only for this package.
    /// </summary>
    private static string Normalize(string name) => VocabularyName.Normalize(name);

    /// <summary>
    /// A literal holding text written for a reader — a name, a label, a claim — in Normalization Form
    /// C, as RDF 1.1 asks of a literal's lexical form. Identifiers (record ids, field names) go through
    /// <see cref="Literal"/> as given: a consumer joins back on them, and must find them unchanged.
    /// </summary>
    private static string Text(string value) => Literal(TextForm.Canonical(value));

    private static string Iri(string iri) => "<" + iri + ">";

    /// <summary>A Turtle <c>STRING_LITERAL_QUOTE</c>: the characters the grammar forbids unescaped, escaped.</summary>
    private static string Literal(string value)
    {
        var builder = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        builder.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        return builder.Append('"').ToString();
    }

    /// <summary>Mints the IRIs of one export's classes and properties, once per name.</summary>
    private sealed class TermSet(string baseIri)
    {
        public bool TryAdd(Dictionary<string, string> seen, string name, VocabularyOrigin origin, bool isClass, out string iri)
        {
            var key = Normalize(name);
            if (seen.TryGetValue(key, out var existing))
            {
                iri = existing;
                return false;
            }

            iri = origin == VocabularyOrigin.Innate
                ? Term(EyuVocabulary.Namespace, "eyu:", Canonical(name))
                : Term(baseIri, ":", Acquired(name, key, isClass));
            seen[key] = iri;
            return true;
        }

        // An acquired name is minted from its comparison key, not from the spelling one proposal happened
        // to use, so "Work Order" in one export and "WorkOrder" in the next are one term -- the same rule
        // that makes them one individual's type. The key keeps no word boundaries, so the only casing
        // left to choose is the first letter: upper for a class, lower for a property (the OWL
        // convention), which also keeps a class and a property that compare alike on separate IRIs.
        // The spelling the proposal used stays as the term's rdfs:label. A name with no letter or digit
        // has no key and is written as it came.
        private static string Acquired(string name, string key, bool isClass)
            => TextForm.Canonical(key.Length == 0 ? name
                : isClass ? char.ToUpperInvariant(key[0]) + key[1..]
                : key);

        // An innate name is written in the spelling the innate vocabulary gives it, so part_of from one
        // proposal and PartOf from another land on the same Eyu term.
        private static string Canonical(string name)
        {
            var key = Normalize(name);
            return InnateVocabulary.EntityTypes.Concat(InnateVocabulary.RelationNames)
                .FirstOrDefault(n => Normalize(n) == key) ?? TextForm.Canonical(name);
        }

        // A prefixed name when the local part is plainly safe in one; the full IRI otherwise, so no
        // name — Korean, spaced, punctuated — depends on the prefixed-name escaping rules.
        private static string Term(string ns, string prefix, string name)
        {
            var local = LocalName(name);
            return IsPlainLocalName(local) ? prefix + local : Iri(ns + local);
        }

        private static bool IsPlainLocalName(string local)
            => local.Length > 0
               && (char.IsAsciiLetter(local[0]) || local[0] == '_')
               && local.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

        // Letters, digits, '_' and '-' stay as written (non-ASCII letters included — an IRI may carry
        // them); every other character is percent-encoded as UTF-8, so nothing the IRIREF grammar
        // forbids can reach the output.
        public static string LocalName(string name)
        {
            var builder = new StringBuilder(name.Length);
            Span<byte> bytes = stackalloc byte[4];
            foreach (var rune in name.EnumerateRunes())
            {
                if (Rune.IsLetterOrDigit(rune) || rune.Value is '_' or '-')
                {
                    builder.Append(rune.ToString());
                    continue;
                }

                var length = rune.EncodeToUtf8(bytes);
                foreach (var b in bytes[..length])
                {
                    builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
                }
            }

            return builder.ToString();
        }
    }
}
