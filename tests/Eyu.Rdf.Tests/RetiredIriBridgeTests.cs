using Eyu.Core.Grounding;
using Eyu.Core.Inference;
using Eyu.Core.Proposals;
using VDS.RDF;
using VDS.RDF.Parsing;
using Xunit;

namespace Eyu.Rdf.Tests;

/// <summary>
/// Before 0.5.0 an acquired class or property was minted from the spelling a proposal used; a store that
/// merged such an export into its default graph holds the old term beside the current one with nothing
/// telling them apart. With <see cref="RdfExportOptions.BridgeRetiredIris"/> each old term is written as
/// deprecated and replaced by the current one — the retirement a published ontology gives a renamed term.
/// </summary>
public class RetiredIriBridgeTests
{
    private const string Base = "https://example.org/plant#";
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private const string Owl = "http://www.w3.org/2002/07/owl#";
    private const string IsReplacedBy = "http://purl.org/dc/terms/isReplacedBy";

    private static readonly RdfExportOptions Plain = new(new Uri(Base));
    private static readonly RdfExportOptions Bridged = new(new Uri(Base)) { BridgeRetiredIris = true };

    private static EntityProposal Entity(string id, string name, string type) =>
        EntityProposal.Create(id, name, type, GroundedClaim.Create($"{name} is a {type}", [new SourceRef($"r-{id}")]),
            InnateVocabulary.OfEntityType(type), 0.8, ProposalBasis.Inferred);

    private static RelationProposal Relation(string name, string from, string to) =>
        RelationProposal.Create(name, from, to, GroundedClaim.Create($"{from} {name} {to}", [new SourceRef($"r-{from}")]),
            InnateVocabulary.OfRelationName(name), 0.6);

    private static readonly OntologyProposal Shop = new(
        [Entity("e1", "WO-1", "Work Order"), Entity("e2", "Press 3", "Machine"), Entity("e3", "Kim", "Person")],
        [Relation("is scheduled on", "e1", "e2"), Relation("maintains", "e3", "e2")],
        []);

    private static IGraph Parse(OntologyProposal proposal, RdfExportOptions options)
    {
        var graph = new Graph();
        new TurtleParser().Load(graph, new StringReader(OntologyTurtle.ToTurtle(proposal, options)));
        return graph;
    }

    private static INode U(IGraph g, string iri) => g.CreateUriNode(new Uri(iri));

    private static IEnumerable<string> Deprecated(IGraph g) =>
        g.GetTriplesWithPredicate(U(g, Owl + "deprecated")).Select(t => ((IUriNode)t.Subject).Uri.AbsoluteUri).Order(StringComparer.Ordinal);

    [Fact]
    public void Off_by_default_the_export_is_unchanged()
    {
        var plain = OntologyTurtle.ToTurtle(Shop, Plain);

        Assert.DoesNotContain("dcterms", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("deprecated", plain, StringComparison.Ordinal);
        Assert.Equal(plain, OntologyTurtle.ToTurtle(Shop, new RdfExportOptions(new Uri(Base)) { BridgeRetiredIris = false }));
    }

    [Fact]
    public void Each_acquired_term_the_previous_rule_spelled_differently_is_deprecated_and_replaced()
    {
        var g = Parse(Shop, Bridged);

        Assert.Equal([Base + "Work%20Order", Base + "is%20scheduled%20on"], Deprecated(g));

        var oldClass = U(g, Base + "Work%20Order");
        Assert.Contains(g.GetTriplesWithSubjectPredicate(oldClass, U(g, Rdf + "type")), t => t.Object.Equals(U(g, Owl + "Class")));
        Assert.Equal(U(g, Base + "Workorder"), Assert.Single(g.GetTriplesWithSubjectPredicate(oldClass, U(g, IsReplacedBy))).Object);

        var oldProperty = U(g, Base + "is%20scheduled%20on");
        Assert.Contains(g.GetTriplesWithSubjectPredicate(oldProperty, U(g, Rdf + "type")), t => t.Object.Equals(U(g, Owl + "ObjectProperty")));
        Assert.Equal(U(g, Base + "isscheduledon"), Assert.Single(g.GetTriplesWithSubjectPredicate(oldProperty, U(g, IsReplacedBy))).Object);

        Assert.Contains(g.GetTriplesWithPredicateObject(U(g, Rdf + "type"), U(g, Owl + "AnnotationProperty")),
            t => t.Subject.Equals(U(g, IsReplacedBy)));
    }

    [Fact]
    public void A_term_both_rules_mint_alike_and_innate_terms_are_not_bridged()
    {
        var g = Parse(Shop, Bridged);

        // "Machine" is its own comparison key's spelling; "maintains" and "Person" are Eyu's innate terms.
        Assert.DoesNotContain(Base + "Machine", Deprecated(g));
        Assert.DoesNotContain(g.Triples, t => t.Subject is IUriNode node && node.Uri.AbsoluteUri.StartsWith("https://ontology.iyulab.com", StringComparison.Ordinal)
            && t.Predicate.Equals(U(g, Owl + "deprecated")));
    }

    /// <summary>
    /// The old spelling of a property can be exactly the IRI a current class has: «Workorder» as a relation
    /// name was minted <c>:Workorder</c> before, which is the class «work order» now. A retired name must
    /// never deprecate a live term.
    /// </summary>
    [Fact]
    public void An_old_iri_that_a_current_term_holds_is_never_deprecated()
    {
        var clash = new OntologyProposal(
            [Entity("e1", "WO-1", "work order"), Entity("e2", "Press 3", "Machine")],
            [Relation("Workorder", "e1", "e2")],
            []);

        var g = Parse(clash, Bridged);

        Assert.DoesNotContain(Base + "Workorder", Deprecated(g));
        Assert.Contains(g.GetTriplesWithPredicateObject(U(g, Rdf + "type"), U(g, Owl + "Class")), t => t.Subject.Equals(U(g, Base + "Workorder")));
    }
}
