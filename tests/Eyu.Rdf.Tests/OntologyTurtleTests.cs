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

    [Fact]
    public void Types_become_classes_relation_names_object_properties_and_entities_individuals()
    {
        var g = Parse(Plant);

        Assert.Single(SubjectsOfType(g, Owl + "Ontology"));
        Assert.Equal(3, SubjectsOfType(g, Owl + "Class").Count());
        Assert.Equal(2, SubjectsOfType(g, Owl + "ObjectProperty").Count());
        Assert.Equal(3, SubjectsOfType(g, Owl + "NamedIndividual").Count());

        var pump = U(g, Base + "entity/e1");
        Assert.Contains(g.GetTriplesWithSubjectPredicate(pump, U(g, Rdf + "type")), t => t.Object.Equals(U(g, Base + "Equipment")));
        Assert.Contains(g.GetTriplesWithSubjectPredicate(pump, U(g, Rdfs + "label")),
            t => t.Object is ILiteralNode { Value: "Pump P-101" });
    }

    [Fact]
    public void A_relation_is_an_assertion_between_individuals_with_its_provenance_on_a_reified_statement()
    {
        var g = Parse(Plant);
        var maintains = U(g, Base + "maintains");

        Assert.Single(g.GetTriplesWithPredicate(maintains));
        var statements = SubjectsOfType(g, Rdf + "Statement").ToList();
        Assert.Equal(2, statements.Count);

        var reified = statements.Single(s =>
            g.GetTriplesWithSubjectPredicate(s, U(g, Rdf + "predicate")).Single().Object.Equals(maintains));
        Assert.Equal(U(g, Base + "entity/e3"), g.GetTriplesWithSubjectPredicate(reified, U(g, Rdf + "subject")).Single().Object);
        var cites = g.GetTriplesWithSubjectPredicate(reified, U(g, EyuVocabulary.Cites)).Single().Object;
        Assert.Equal("owner", ((ILiteralNode)g.GetTriplesWithSubjectPredicate(cites, U(g, EyuVocabulary.FieldName)).Single().Object).Value);
        Assert.Equal("0.6", ((ILiteralNode)g.GetTriplesWithSubjectPredicate(reified, U(g, EyuVocabulary.Confidence)).Single().Object).Value);
    }

    // Innate words are Eyu's own, so they are written in Eyu's namespace — and nowhere else. Aligning
    // Person to another vocabulary's Person would be term normalization the library does not perform.
    [Fact]
    public void Innate_terms_are_Eyus_own_and_acquired_terms_are_the_callers()
    {
        var g = Parse(Plant);

        Assert.Contains(SubjectsOfType(g, Owl + "Class"), n => n.Equals(U(g, EyuVocabulary.Namespace + "Person")));
        Assert.Contains(SubjectsOfType(g, Owl + "ObjectProperty"), n => n.Equals(U(g, EyuVocabulary.Namespace + "PartOf")));
        Assert.Contains(SubjectsOfType(g, Owl + "Class"), n => n.Equals(U(g, Base + "ProductionLine")));
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
        Assert.Equal(U(g, Base + "WorkOrder"),
            g.GetTriplesWithSubjectPredicate(U(g, Base + "entity/b"), U(g, Rdf + "type"))
                .Single(t => !t.Object.Equals(U(g, Owl + "NamedIndividual"))).Object);
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

        Assert.Equal("declared", ((ILiteralNode)g.GetTriplesWithSubjectPredicate(U(g, Base + "entity/e2"), U(g, EyuVocabulary.Basis)).Single().Object).Value);
        Assert.Equal("innate", ((ILiteralNode)g.GetTriplesWithSubjectPredicate(U(g, EyuVocabulary.Namespace + "Person"), U(g, EyuVocabulary.Origin)).Single().Object).Value);
        Assert.Equal("acquired", ((ILiteralNode)g.GetTriplesWithSubjectPredicate(U(g, Base + "Equipment"), U(g, EyuVocabulary.Origin)).Single().Object).Value);
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
