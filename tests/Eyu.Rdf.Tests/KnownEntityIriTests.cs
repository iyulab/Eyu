using Eyu.Core.Grounding;
using Eyu.Core.Proposals;
using VDS.RDF;
using VDS.RDF.Parsing;
using Xunit;

namespace Eyu.Rdf.Tests;

/// <summary>
/// An entity matched to a known entity is written as that entity: under the known entity's key, not under
/// the IRI its name, type and records would mint. This is what lets a caller that keeps exports join a
/// later proposal to an earlier one without renaming anything.
/// </summary>
public class KnownEntityIriTests
{
    private const string Base = "https://example.org/plant#";

    private static readonly RdfExportOptions Options = new(new Uri(Base));

    private static EntityProposal Entity(string id, string name, string type, string? knownKey = null) =>
        EntityProposal.Create(id, name, type, GroundedClaim.Create($"{name} is a {type}", [new SourceRef($"r-{id}")]),
            InnateVocabulary.OfEntityType(type), 0.8, knownEntityKey: knownKey);

    private static OntologyProposal Proposal(params EntityProposal[] entities) => new(entities, [], []);

    [Fact]
    public void A_known_key_that_is_an_iri_is_the_individuals_iri()
    {
        const string earlier = "https://example.org/plant#entity/4d3c2b1a00000000000000000000000f";

        var iris = OntologyTurtle.IndividualIris(Proposal(Entity("e1", "프레스#4", "Equipment", earlier)), Options);

        Assert.Equal(earlier, iris["e1"]);
    }

    [Fact]
    public void A_match_keeps_the_iri_an_earlier_export_minted_for_the_entity()
    {
        // The first export mints from name, type and records; a caller keeps that IRI as the known key;
        // the second call's entity, named differently from other records, is written under it.
        var first = Proposal(Entity("e1", "프레스 4호기", "Asset"));
        var minted = OntologyTurtle.IndividualIris(first, Options)["e1"];

        var second = Proposal(Entity("x7", "Press 04 (A line)", "Equipment", minted));

        Assert.Equal(minted, OntologyTurtle.IndividualIris(second, Options)["x7"]);
    }

    [Fact]
    public void An_opaque_key_is_hashed_into_its_own_branch_the_same_in_every_export()
    {
        var once = OntologyTurtle.IndividualIris(Proposal(Entity("e1", "A", "Equipment", "PRS-004")), Options)["e1"];
        var again = OntologyTurtle.IndividualIris(Proposal(Entity("z9", "Other name", "Machine", "PRS-004")), Options)["z9"];

        Assert.Equal(once, again);
        Assert.StartsWith(Base + "entity/known/", once, StringComparison.Ordinal);
        Assert.DoesNotContain("PRS-004", once, StringComparison.Ordinal);
    }

    [Fact]
    public void Entities_without_a_known_key_are_minted_as_before()
    {
        var plain = Entity("e1", "Press 4", "Equipment");
        var alongside = Entity("e2", "Conveyor 12", "Equipment", "urn:example:conveyor-12");

        var alone = OntologyTurtle.IndividualIris(Proposal(plain), Options)["e1"];
        var mixed = OntologyTurtle.IndividualIris(Proposal(plain, alongside), Options);

        Assert.Equal(alone, mixed["e1"]);
        Assert.Equal("urn:example:conveyor-12", mixed["e2"]);
    }

    [Fact]
    public void Two_entities_matched_to_one_known_entity_are_one_individual_carrying_both_claims()
    {
        var proposal = Proposal(Entity("e1", "프레스#4", "Equipment", "PRS-004"), Entity("e2", "Press 04", "Equipment", "PRS-004"));

        var graph = new Graph();
        new TurtleParser().Load(graph, new StringReader(OntologyTurtle.ToTurtle(proposal, Options)));

        var iris = OntologyTurtle.IndividualIris(proposal, Options);
        Assert.Equal(iris["e1"], iris["e2"]);
        var individual = graph.CreateUriNode(new Uri(iris["e1"]));
        Assert.Equal(2, graph.GetTriplesWithSubjectPredicate(individual, graph.CreateUriNode(new Uri(EyuVocabulary.Claim))).Count());
    }
}
