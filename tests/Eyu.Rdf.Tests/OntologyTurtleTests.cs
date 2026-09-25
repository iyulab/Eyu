using System.Text;
using Eyu.Core.Grounding;
using Eyu.Core.Proposals;
using VDS.RDF;
using VDS.RDF.Parsing;
using Xunit;

namespace Eyu.Rdf.Tests;

/// <summary>
/// Every assertion reads the output back through an independent Turtle parser: a writer checked only
/// against its own idea of the grammar would pass the one mistake that matters, output nothing else
/// can read.
/// </summary>
public class OntologyTurtleTests
{
    private const string Base = "https://example.org/plant#";
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private const string Rdfs = "http://www.w3.org/2000/01/rdf-schema#";
    private const string Owl = "http://www.w3.org/2002/07/owl#";

    private static readonly RdfExportOptions Options = new(new Uri(Base));

    private static GroundedClaim Cite(string claim, params SourceRef[] sources) => GroundedClaim.Create(claim, sources);

    private static EntityProposal Entity(string id, string name, string type, double confidence = 0.8, ProposalBasis basis = ProposalBasis.Inferred) =>
        EntityProposal.Create(id, name, type, Cite($"{name} is a {type}", new SourceRef($"r-{id}")),
            InnateVocabulary.OfEntityType(type), confidence, basis);

    private static RelationProposal Relation(string name, string from, string to) =>
        RelationProposal.Create(name, from, to, Cite($"{from} {name} {to}", new SourceRef($"r-{from}", "owner")),
            InnateVocabulary.OfRelationName(name), 0.6);

    private static IGraph Parse(OntologyProposal proposal)
    {
        var graph = new Graph();
        new TurtleParser().Load(graph, new StringReader(OntologyTurtle.ToTurtle(proposal, Options)));
        return graph;
    }

    private static INode U(IGraph g, string iri) => g.CreateUriNode(new Uri(iri));

    private static INode Individual(IGraph g, OntologyProposal proposal, string entityId) =>
        g.CreateUriNode(new Uri(OntologyTurtle.IndividualIris(proposal, Options)[entityId]));

    private static IEnumerable<INode> SubjectsOfType(IGraph g, string type) =>
        g.GetTriplesWithPredicateObject(U(g, Rdf + "type"), U(g, type)).Select(t => t.Subject);

    private static readonly OntologyProposal Plant = new(
        [
            Entity("e1", "Pump P-101", "Equipment"),
            Entity("e2", "Line 3", "ProductionLine", basis: ProposalBasis.Declared),
            Entity("e3", "Kim", "Person"),
        ],
        [
            Relation("PartOf", "e1", "e2"),
            Relation("maintains", "e3", "e1"),
        ],
        []);

    // The rule that mints IRIs changed in 0.4.0 (individuals) and again in 0.5.0 (acquired terms,
    // Unicode form). Individual IRIs survive such a change and term IRIs do not, so a store holding
    // exports of both rules types one individual into the old class and the new one; the header
    // says which rule an export used, so the old triples can be found and retired. A change to any
    // minting rule changes this value — and this test with it.
    [Fact]
    public void The_ontology_header_names_the_iri_rule_its_terms_and_individuals_were_minted_under()
    {
        var g = Parse(Plant);

        var ontology = Assert.Single(SubjectsOfType(g, Owl + "Ontology"));
        Assert.Equal(U(g, "https://example.org/plant"), ontology);
        var rule = Assert.Single(g.GetTriplesWithSubjectPredicate(ontology, U(g, EyuVocabulary.IriRule))).Object;
        Assert.Equal("0.5.0", Assert.IsAssignableFrom<ILiteralNode>(rule).Value);
        Assert.Contains(SubjectsOfType(g, Owl + "AnnotationProperty"), n => n.Equals(U(g, EyuVocabulary.IriRule)));
    }

    [Fact]
    public void Types_become_classes_relation_names_object_properties_and_entities_individuals()
    {
        var g = Parse(Plant);

        Assert.Single(SubjectsOfType(g, Owl + "Ontology"));
        Assert.Equal(3, SubjectsOfType(g, Owl + "Class").Count());
        Assert.Equal(2, SubjectsOfType(g, Owl + "ObjectProperty").Count());
        Assert.Equal(3, SubjectsOfType(g, Owl + "NamedIndividual").Count());

        var pump = Individual(g, Plant, "e1");
        Assert.Contains(g.GetTriplesWithSubjectPredicate(pump, U(g, Rdf + "type")), t => t.Object.Equals(U(g, Base + "Equipment")));
        Assert.Contains(g.GetTriplesWithSubjectPredicate(pump, U(g, Rdfs + "label")),
            t => t.Object is ILiteralNode { Value: "Pump P-101" });
    }

    [Fact]
    public void A_relation_is_an_assertion_between_individuals_with_its_provenance_as_an_axiom_annotation()
    {
        var g = Parse(Plant);
        var maintains = U(g, Base + "maintains");

        Assert.Single(g.GetTriplesWithPredicate(maintains));
        var axioms = SubjectsOfType(g, Owl + "Axiom").ToList();
        Assert.Equal(2, axioms.Count);

        var annotated = axioms.Single(s =>
            g.GetTriplesWithSubjectPredicate(s, U(g, Owl + "annotatedProperty")).Single().Object.Equals(maintains));
        Assert.Equal(Individual(g, Plant, "e3"), g.GetTriplesWithSubjectPredicate(annotated, U(g, Owl + "annotatedSource")).Single().Object);
        Assert.Equal(Individual(g, Plant, "e1"), g.GetTriplesWithSubjectPredicate(annotated, U(g, Owl + "annotatedTarget")).Single().Object);
        var cites = g.GetTriplesWithSubjectPredicate(annotated, U(g, EyuVocabulary.Cites)).Single().Object;
        Assert.Equal("owner", ((ILiteralNode)g.GetTriplesWithSubjectPredicate(cites, U(g, EyuVocabulary.FieldName)).Single().Object).Value);
        Assert.Equal("0.6", ((ILiteralNode)g.GetTriplesWithSubjectPredicate(annotated, U(g, EyuVocabulary.Confidence)).Single().Object).Value);
    }

    // An OWL reader maps only the rdf: terms OWL 2 gives a meaning to. Anything else from that namespace
    // — rdf:Statement and its rdf:subject/predicate/object above all — is reserved vocabulary: an OWL
    // parser leaves those triples unparsed and the ontology falls outside OWL 2 DL, which a plain RDF
    // parser, reading every triple alike, never notices.
    [Fact]
    public void The_only_rdf_term_written_is_rdf_type_so_an_owl_reader_parses_every_triple()
    {
        var g = Parse(Plant);

        var rdfTerms = g.Triples
            .SelectMany(t => new[] { t.Predicate, t.Object })
            .OfType<IUriNode>()
            .Select(n => n.Uri.AbsoluteUri)
            .Where(iri => iri.StartsWith(Rdf, StringComparison.Ordinal))
            .Distinct();

        Assert.Equal([Rdf + "type"], rdfTerms);
    }

    // Innate words are Eyu's own, so they are written in Eyu's namespace — and nowhere else. Aligning
    // Person to another vocabulary's Person would be term normalization the library does not perform.
    [Fact]
    public void Innate_terms_are_Eyus_own_and_acquired_terms_are_the_callers()
    {
        var g = Parse(Plant);

        Assert.Contains(SubjectsOfType(g, Owl + "Class"), n => n.Equals(U(g, EyuVocabulary.Namespace + "Person")));
        Assert.Contains(SubjectsOfType(g, Owl + "ObjectProperty"), n => n.Equals(U(g, EyuVocabulary.Namespace + "PartOf")));
        Assert.Contains(SubjectsOfType(g, Owl + "Class"), n => n.Equals(U(g, Base + "Productionline")));
        Assert.DoesNotContain(g.Triples, t => t.Object is IUriNode u
            && !u.Uri.AbsoluteUri.StartsWith(Base, StringComparison.Ordinal)
            && !u.Uri.AbsoluteUri.StartsWith(EyuVocabulary.Namespace, StringComparison.Ordinal)
            && !u.Uri.AbsoluteUri.StartsWith("http://www.w3.org/", StringComparison.Ordinal));
    }

    [Fact]
    public void Names_Eyu_compares_as_one_are_one_class_and_innate_spellings_are_canonical()
    {
        var proposal = new OntologyProposal(
            [Entity("a", "WO-1", "WorkOrder"), Entity("b", "WO-2", "work_order"), Entity("c", "Plant", "Location")],
            [Relation("located_in", "a", "c"), Relation("LocatedIn", "b", "c")],
            []);

        var g = Parse(proposal);

        Assert.Equal(2, SubjectsOfType(g, Owl + "Class").Count());
        Assert.Single(SubjectsOfType(g, Owl + "ObjectProperty"));
        Assert.Equal(2, g.GetTriplesWithPredicate(U(g, EyuVocabulary.Namespace + "LocatedIn")).Count());
        Assert.Equal(U(g, Base + "Workorder"),
            g.GetTriplesWithSubjectPredicate(Individual(g, proposal, "b"), U(g, Rdf + "type"))
                .Single(t => !t.Object.Equals(U(g, Owl + "NamedIndividual"))).Object);
    }

    // The individual's IRI compares type names as Eyu compares them, so two exports that spell a type
    // differently name one individual. The class it is typed under has to follow the same rule, or a
    // triple store merging the exports puts that one individual in two unrelated classes.
    [Fact]
    public void Two_exports_that_spell_a_type_differently_merge_into_one_class()
    {
        var first = new OntologyProposal([Entity("a", "Replace bearing", "Work Order")], [Relation("maintains", "a", "a")], []);
        var second = new OntologyProposal([Entity("a", "Replace bearing", "WorkOrder")], [Relation("Maintains", "a", "a")], []);

        var merged = new Graph();
        foreach (var proposal in new[] { first, second })
        {
            new TurtleParser().Load(merged, new StringReader(OntologyTurtle.ToTurtle(proposal, Options)));
        }

        var type = Assert.Single(SubjectsOfType(merged, Owl + "Class"));
        Assert.Equal(U(merged, Base + "Workorder"), type);
        var individual = Assert.Single(SubjectsOfType(merged, Owl + "NamedIndividual"));
        Assert.Equal([type], merged.GetTriplesWithSubjectPredicate(individual, U(merged, Rdf + "type"))
            .Select(t => t.Object).Where(o => !o.Equals(U(merged, Owl + "NamedIndividual"))));
        Assert.Equal(U(merged, Base + "maintains"), Assert.Single(SubjectsOfType(merged, Owl + "ObjectProperty")));
        Assert.Equal(["Work Order", "WorkOrder"], merged.GetTriplesWithSubjectPredicate(type, U(merged, Rdfs + "label"))
            .Select(t => ((ILiteralNode)t.Object).Value).Order(StringComparer.Ordinal));
    }

    // The same comparison reaches below spelling: Hangul decomposed (NFD, how text saved on macOS
    // commonly arrives) and precomposed render identically, and full-width Latin is the same letters.
    // Each pair must land on one class and one individual, and nothing written may be left un-NFC —
    // RDF 1.1 Concepts asks IRIs and literal lexical forms to be in Normalization Form C.
    [Theory]
    [InlineData("작업지시", "NFD")]
    [InlineData("Work Order", "Ｗｏｒｋ Ｏｒｄｅｒ")]
    public void Two_exports_that_write_a_type_in_different_unicode_forms_merge_into_one_class(string type, string other)
    {
        var otherType = other == "NFD" ? type.Normalize(NormalizationForm.FormD) : other;
        var first = new OntologyProposal([Entity("a", "베어링 교체", type)], [], []);
        var second = new OntologyProposal([Entity("a", "베어링 교체".Normalize(NormalizationForm.FormD), otherType)], [], []);

        var merged = new Graph();
        foreach (var proposal in new[] { first, second })
        {
            var turtle = OntologyTurtle.ToTurtle(proposal, Options);
            Assert.True(turtle.IsNormalized(NormalizationForm.FormC));
            new TurtleParser().Load(merged, new StringReader(turtle));
        }

        Assert.Single(SubjectsOfType(merged, Owl + "Class"));
        var individual = Assert.Single(SubjectsOfType(merged, Owl + "NamedIndividual"));
        Assert.Equal("베어링 교체", ((ILiteralNode)Assert.Single(merged.GetTriplesWithSubjectPredicate(individual, U(merged, Rdfs + "label"))).Object).Value);
    }

    // A record id is an identifier a consumer joins back on, not text: it is written exactly as given.
    [Fact]
    public void A_cited_record_id_is_written_as_given_even_when_it_is_not_NFC()
    {
        var recordId = "문서-1".Normalize(NormalizationForm.FormD);
        var proposal = new OntologyProposal(
            [EntityProposal.Create("a", "Pump", "Equipment", Cite("pump", new SourceRef(recordId)), VocabularyOrigin.Acquired, 0.5)], [], []);

        var g = Parse(proposal);

        var cited = g.GetTriplesWithPredicate(U(g, EyuVocabulary.RecordId)).Select(t => ((ILiteralNode)t.Object).Value);
        Assert.Equal([recordId], cited);
    }

    [Fact]
    public void A_class_and_a_property_that_compare_alike_stay_apart()
    {
        var proposal = new OntologyProposal([Entity("a", "Kim", "Owner"), Entity("b", "Pump", "Equipment")], [Relation("owner", "a", "b")], []);

        var g = Parse(proposal);

        Assert.Contains(SubjectsOfType(g, Owl + "Class"), n => n.Equals(U(g, Base + "Owner")));
        Assert.Equal(U(g, Base + "owner"), Assert.Single(SubjectsOfType(g, Owl + "ObjectProperty")));
    }

    // Korean, spaces and punctuation in a name, and quotes, backslashes and line breaks in a claim, are
    // the inputs a hand-written writer gets wrong. Each must survive a parse unchanged.
    [Fact]
    public void Names_and_claims_outside_plain_ascii_round_trip_through_a_parser()
    {
        const string claim = "문서에 \"펌프\" 라고\n적혀 있다 \\ 확인\t끝";
        var proposal = new OntologyProposal(
            [EntityProposal.Create("회사 #1", "(주)이유", "협력 업체/공급사", Cite(claim, new SourceRef("doc 7", "본문")), VocabularyOrigin.Acquired, 0.35)],
            [],
            []);

        var g = Parse(proposal);

        var individual = Assert.Single(SubjectsOfType(g, Owl + "NamedIndividual"));
        Assert.Equal("(주)이유", ((ILiteralNode)g.GetTriplesWithSubjectPredicate(individual, U(g, Rdfs + "label")).Single().Object).Value);
        Assert.Equal(claim, ((ILiteralNode)g.GetTriplesWithSubjectPredicate(individual, U(g, EyuVocabulary.Claim)).Single().Object).Value);
        var type = Assert.Single(SubjectsOfType(g, Owl + "Class"));
        Assert.Equal("협력 업체/공급사", ((ILiteralNode)g.GetTriplesWithSubjectPredicate(type, U(g, Rdfs + "label")).Single().Object).Value);
    }

    [Fact]
    public void Basis_and_origin_are_carried_as_annotations()
    {
        var g = Parse(Plant);

        Assert.Equal("declared", ((ILiteralNode)g.GetTriplesWithSubjectPredicate(Individual(g, Plant, "e2"), U(g, EyuVocabulary.Basis)).Single().Object).Value);
        Assert.Equal("innate", ((ILiteralNode)g.GetTriplesWithSubjectPredicate(U(g, EyuVocabulary.Namespace + "Person"), U(g, EyuVocabulary.Origin)).Single().Object).Value);
        Assert.Equal("acquired", ((ILiteralNode)g.GetTriplesWithSubjectPredicate(U(g, Base + "Equipment"), U(g, EyuVocabulary.Origin)).Single().Object).Value);
    }

    // Which cited records are the individual itself travels with it, so a reader of the ontology can
    // tell the records that are one entity from the records that only mention it -- the distinction
    // the proposal's confidence already rests on.
    [Fact]
    public void The_records_that_denote_an_individual_are_annotated_apart_from_those_that_mention_it()
    {
        var machine = EntityProposal.Create("m", "Press 3", "Machine",
            Cite("Press 3 appears in two work orders and the machine master", new SourceRef("w-01"), new SourceRef("w-02"), new SourceRef("m-01")),
            VocabularyOrigin.Acquired, 0.9, denotedBy: ["m-01"]);

        var proposal = new OntologyProposal([machine], [], []);
        var g = Parse(proposal);

        var individual = Individual(g, proposal, "m");
        Assert.Equal(["m-01"], g.GetTriplesWithSubjectPredicate(individual, U(g, EyuVocabulary.DenotedBy)).Select(t => ((ILiteralNode)t.Object).Value));
        Assert.Equal(3, g.GetTriplesWithSubjectPredicate(individual, U(g, EyuVocabulary.Cites)).Count());
        Assert.Contains(g.GetTriplesWithSubjectPredicate(U(g, EyuVocabulary.DenotedBy), U(g, Rdf + "type")), t => t.Object.Equals(U(g, Owl + "AnnotationProperty")));
    }

    // The model picks an entity's id afresh on every call, so an IRI minted from it never carried over
    // from one export to the next. Its name, type and denoting records are what a second proposal over the
    // same records repeats.
    [Fact]
    public void An_individual_is_named_by_its_name_type_and_denoting_records_whatever_id_the_model_gave_it()
    {
        EntityProposal Machine(string id, string name, params string[] denotedBy) => EntityProposal.Create(id, name, "Machine",
            Cite("a machine", [.. denotedBy.Select(r => new SourceRef(r)), new SourceRef("w-01")]), VocabularyOrigin.Acquired, 0.9, denotedBy: denotedBy);
        string Iri(EntityProposal e) => OntologyTurtle.IndividualIris(new OntologyProposal([e], [], []), Options)[e.EntityId];

        var first = Iri(Machine("E1", "Press 3", "m-01", "m-02"));

        Assert.Equal(first, Iri(Machine("machine_1", "press-3", "m-02", "m-01")));
        Assert.NotEqual(first, Iri(Machine("E1", "Press 3", "m-01")));
        Assert.NotEqual(first, Iri(Machine("E1", "Press No. 3", "m-01", "m-02")));
        Assert.StartsWith(Base + "entity/", first, StringComparison.Ordinal);
        Assert.DoesNotContain("m-01", first, StringComparison.Ordinal);
    }

    [Fact]
    public void An_individual_no_record_denotes_is_named_by_its_name_and_type_compared_as_Eyu_compares_them()
    {
        EntityProposal Chunked(string id, string name, string type) => EntityProposal.Create(id, name, type,
            Cite("the report names it", new SourceRef("chunk-3")), VocabularyOrigin.Acquired, 0.7, denotedBy: []);

        string Iri(EntityProposal e) => OntologyTurtle.IndividualIris(new OntologyProposal([e], [], []), Options)[e.EntityId];

        Assert.Equal(Iri(Chunked("E4", "Korea Hydro", "Company")), Iri(Chunked("org_2", "korea-hydro", "company")));
        Assert.NotEqual(Iri(Chunked("E4", "Korea Hydro", "Company")), Iri(Chunked("E4", "Korea Hydro", "Regulator")));
        Assert.NotEqual(Iri(Chunked("E4", "Korea Hydro", "Company")), Iri(Chunked("E4", "Korea Nuclear", "Company")));
    }

    // A model reading a document chunk by chunk proposes one company once per chunk it appears in. With no
    // record denoting either, nothing tells the two apart — they are one individual carrying both claims.
    [Fact]
    public void Entities_one_proposal_names_alike_and_no_record_denotes_are_one_individual()
    {
        EntityProposal Named(string id, string chunk) => EntityProposal.Create(id, "Kim", "Person",
            Cite($"{chunk} names Kim", new SourceRef(chunk)), VocabularyOrigin.Innate, 0.6, denotedBy: []);
        var proposal = new OntologyProposal([Named("p1", "chunk-1"), Named("p2", "chunk-2"), Entity("e1", "Pump P-101", "Equipment")], [Relation("maintains", "p2", "e1")], []);

        var iris = OntologyTurtle.IndividualIris(proposal, Options);
        var g = Parse(proposal);

        Assert.Equal(iris["p1"], iris["p2"]);
        Assert.DoesNotContain("/local/", iris["p1"], StringComparison.Ordinal);
        Assert.Equal(2, SubjectsOfType(g, Owl + "NamedIndividual").Count());
        var kim = Individual(g, proposal, "p1");
        Assert.Equal(2, g.GetTriplesWithSubjectPredicate(kim, U(g, EyuVocabulary.Claim)).Count());
        Assert.Equal(kim, g.GetTriplesWithPredicate(U(g, Base + "maintains")).Single().Subject);
    }

    // One row reporting an event names the aircraft, the part and the event, and a model says the row
    // denotes each of them. The records alone are no identity then; the name and type tell them apart.
    [Fact]
    public void Entities_one_row_is_said_to_denote_are_told_apart_by_name_and_type()
    {
        EntityProposal FromRow(string id, string name, string type) => EntityProposal.Create(id, name, type,
            Cite($"{name} appears in sdr-1", new SourceRef("sdr-1")), VocabularyOrigin.Acquired, 0.7, denotedBy: ["sdr-1"]);
        var proposal = new OntologyProposal(
            [FromRow("a", "Boeing 737-823", "Aircraft"), FromRow("b", "Floor beam", "AircraftPart"), FromRow("c", "Cracked floor beam", "Discrepancy")], [], []);

        var iris = OntologyTurtle.IndividualIris(proposal, Options);

        Assert.Equal(3, iris.Values.Distinct(StringComparer.Ordinal).Count());
        Assert.All(iris.Values, iri => Assert.DoesNotContain("/local/", iri, StringComparison.Ordinal));
        Assert.Equal(3, SubjectsOfType(Parse(proposal), Owl + "NamedIndividual").Count());
    }

    [Fact]
    public void Every_individual_a_relation_names_is_the_one_its_entity_is_written_under()
    {
        var g = Parse(Plant);
        var individuals = SubjectsOfType(g, Owl + "NamedIndividual").ToHashSet();

        Assert.All(g.GetTriplesWithPredicate(U(g, Base + "maintains")).Concat(g.GetTriplesWithPredicate(U(g, EyuVocabulary.Namespace + "PartOf"))),
            t => Assert.True(individuals.Contains(t.Subject) && individuals.Contains(t.Object)));
    }

    // A rejection is a fact about the model's answer, not about the domain: an ontology carrying it
    // would assert what the proposer refused to.
    [Fact]
    public void Rejections_are_not_written()
    {
        var withRejection = Plant with
        {
            Rejections = [new ProposalRejection(ProposalElement.Relation, "rel-9", RejectionReason.DanglingRelationEnd, "points at e404")],
        };

        Assert.Equal(OntologyTurtle.ToTurtle(Plant, Options), OntologyTurtle.ToTurtle(withRejection, Options));
    }

    [Fact]
    public void An_empty_proposal_is_still_an_ontology()
    {
        var g = Parse(new OntologyProposal([], [], []));

        Assert.Single(SubjectsOfType(g, Owl + "Ontology"));
        Assert.Empty(SubjectsOfType(g, Owl + "Class"));
    }

    [Theory]
    [InlineData("https://example.org/plant")]
    [InlineData("urn-without-scheme")]
    public void The_base_iri_must_be_an_absolute_namespace(string baseIri)
    {
        Assert.ThrowsAny<Exception>(() => new RdfExportOptions(new Uri(baseIri, UriKind.RelativeOrAbsolute)));
    }

    [Fact]
    public void A_slash_namespace_is_accepted()
    {
        var g = new Graph();
        new TurtleParser().Load(g, new StringReader(OntologyTurtle.ToTurtle(Plant, new RdfExportOptions(new Uri("https://example.org/plant/")))));

        Assert.Contains(SubjectsOfType(g, Owl + "Class"), n => n.Equals(U(g, "https://example.org/plant/Equipment")));
    }
}
