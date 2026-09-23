using System.Globalization;
using System.Text;
using Eyu.Core.Grounding;
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
/// compares them, case and separators ignored, so <c>WorkOrder</c> and <c>work_order</c> are one class
/// written under the first spelling seen. No domain or range is asserted: the proposal shows which
/// types a relation was seen between, which is evidence, not the axiom a range would state.
/// </para>
/// <para>
/// What the proposal knows about each element travels with it, as annotations in
/// <see cref="EyuVocabulary"/>: the claim, each cited record (and field), the confidence and whether
/// the type was declared. A relation's annotations sit on an <c>rdf:Statement</c> reifying it, since an
/// assertion between two individuals has nowhere else to carry them. Innate types and relations are
/// written as Eyu's own terms and acquired ones under the caller's <see cref="RdfExportOptions.BaseIri"/>
/// — see <see cref="EyuVocabulary"/> for why neither is aligned to an outside vocabulary.
/// </para>
/// <para>
/// <see cref="OntologyProposal.Rejections"/> is not written: a rejection is a fact about the model's
/// answer, not about the domain, and an ontology that carried it would assert what the proposer
/// refused to. A caller keeps it from the proposal itself. An entity's IRI is minted from its
/// <see cref="EntityProposal.EntityId"/>, which means nothing outside the one proposal — two exports
/// of two proposals are two sets of individuals, and merging them is entity resolution, not export.
/// </para>
/// </summary>
public static class OntologyTurtle
{
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private const string Rdfs = "http://www.w3.org/2000/01/rdf-schema#";
    private const string Owl = "http://www.w3.org/2002/07/owl#";
    private const string Xsd = "http://www.w3.org/2001/XMLSchema#";

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

        writer.Write("@prefix rdf: <" + Rdf + "> .\n");
        writer.Write("@prefix rdfs: <" + Rdfs + "> .\n");
        writer.Write("@prefix owl: <" + Owl + "> .\n");
        writer.Write("@prefix xsd: <" + Xsd + "> .\n");
        writer.Write("@prefix eyu: <" + EyuVocabulary.Namespace + "> .\n");
        writer.Write("@prefix : <" + baseIri + "> .\n\n");

        writer.Write(Iri(baseIri.TrimEnd('#')) + " a owl:Ontology .\n\n");

        foreach (var property in new[] { "claim", "cites", "recordId", "fieldName", "confidence", "basis", "origin" })
        {
            writer.Write("eyu:" + property + " a owl:AnnotationProperty .\n");
        }

        writer.Write('\n');

        var classes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entity in proposal.Entities)
        {
            if (terms.TryAdd(classes, entity.EntityType, entity.Origin, out var iri))
            {
                WriteTerm(writer, iri, "owl:Class", entity.EntityType, entity.Origin);
            }
        }

        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var relation in proposal.Relations)
        {
            if (terms.TryAdd(properties, relation.RelationName, relation.Origin, out var iri))
            {
                WriteTerm(writer, iri, "owl:ObjectProperty", relation.RelationName, relation.Origin);
            }
        }

        foreach (var entity in proposal.Entities)
        {
            var subject = Iri(terms.Individual(entity.EntityId));
            writer.Write(subject + " a owl:NamedIndividual, " + classes[Normalize(entity.EntityType)] + " ;\n");
            writer.Write("    rdfs:label " + Literal(entity.Name) + " ;\n");
            WriteProvenance(writer, entity.Claim, entity.Confidence, entity.Basis);
        }

        foreach (var relation in proposal.Relations)
        {
            var from = Iri(terms.Individual(relation.FromEntityId));
            var to = Iri(terms.Individual(relation.ToEntityId));
            var predicate = properties[Normalize(relation.RelationName)];

            writer.Write(from + " " + predicate + " " + to + " .\n");
            writer.Write("[] a rdf:Statement ;\n");
            writer.Write("    rdf:subject " + from + " ;\n");
            writer.Write("    rdf:predicate " + predicate + " ;\n");
            writer.Write("    rdf:object " + to + " ;\n");
            WriteProvenance(writer, relation.Claim, relation.Confidence, relation.Basis);
        }
    }

    private static void WriteTerm(TextWriter writer, string iri, string kind, string label, VocabularyOrigin origin)
    {
        writer.Write(iri + " a " + kind + " ;\n");
        writer.Write("    rdfs:label " + Literal(label) + " ;\n");
        writer.Write("    eyu:origin " + Literal(origin == VocabularyOrigin.Innate ? "innate" : "acquired") + " .\n\n");
    }

    private static void WriteProvenance(TextWriter writer, GroundedClaim claim, double confidence, ProposalBasis basis)
    {
        writer.Write("    eyu:claim " + Literal(claim.Claim) + " ;\n");
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
    /// Eyu's lexical name comparison — case and separators ignored. Restated rather than shared: the
    /// rule is internal to Eyu.Core, and one line of it here is cheaper than a public surface on the
    /// core that exists only for this package.
    /// </summary>
    private static string Normalize(string name)
        => new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

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

    /// <summary>Mints the IRIs of one export: its classes and properties once per name, its individuals per id.</summary>
    private sealed class TermSet(string baseIri)
    {
        public bool TryAdd(Dictionary<string, string> seen, string name, VocabularyOrigin origin, out string iri)
        {
            var key = Normalize(name);
            if (seen.TryGetValue(key, out var existing))
            {
                iri = existing;
                return false;
            }

            iri = origin == VocabularyOrigin.Innate
                ? Term(EyuVocabulary.Namespace, "eyu:", Canonical(name))
                : Term(baseIri, ":", name);
            seen[key] = iri;
            return true;
        }

        public string Individual(string entityId) => baseIri + "entity/" + LocalName(entityId);

        // An innate name is written in the spelling the innate vocabulary gives it, so part_of from one
        // proposal and PartOf from another land on the same Eyu term.
        private static string Canonical(string name)
        {
            var key = Normalize(name);
            return InnateVocabulary.EntityTypes.Concat(InnateVocabulary.RelationNames)
                .FirstOrDefault(n => Normalize(n) == key) ?? name;
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
        private static string LocalName(string name)
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
