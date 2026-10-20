using System.Xml.Linq;
using Xunit;

namespace Eyu.Core.Tests.Quality;

public class Cq4oeCorpusTests
{
    private static readonly XDocument Ontology = XDocument.Parse("""
        <?xml version="1.0"?>
        <rdf:RDF xmlns="http://example.org/wine#" xml:base="http://example.org/wine"
                 xmlns:owl="http://www.w3.org/2002/07/owl#"
                 xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            <owl:DatatypeProperty rdf:about="http://example.org/wine#yearValue"/>
            <owl:NamedIndividual rdf:about="http://example.org/wine#NapaRegion">
                <rdf:type rdf:resource="http://example.org/wine#Region"/>
                <locatedIn rdf:resource="http://example.org/wine#CaliforniaRegion"/>
            </owl:NamedIndividual>
            <owl:NamedIndividual rdf:about="http://example.org/wine#BancroftChardonnay">
                <rdf:type rdf:resource="http://example.org/wine#Chardonnay"/>
                <hasBody rdf:resource="http://example.org/wine#Medium"/>
                <locatedIn rdf:resource="http://example.org/wine#NapaRegion"/>
                <locatedIn rdf:resource="http://example.org/wine#CaliforniaRegion"/>
            </owl:NamedIndividual>
            <owl:NamedIndividual rdf:about="http://example.org/wine#Year1998">
                <yearValue>1998</yearValue>
            </owl:NamedIndividual>
        </rdf:RDF>
        """);

    private const string Terms = """
        [
          {"id": "CQ1", "question": "Which region is a wine from?", "classes": ["Region", "Wine"], "properties": ["locatedIn"]},
          {"id": "CQ2", "question": "Which year?", "classes": [], "properties": ["yearValue"]}
        ]
        """;

    [Fact]
    public void Individuals_become_records_without_their_type_and_with_every_other_statement()
    {
        var corpus = Cq4oeCase.FromGold("cq4oe-test", Ontology, Terms);

        Assert.Equal(["NapaRegion", "BancroftChardonnay", "Year1998"], corpus.Records.Select(r => r.Id));
        var chardonnay = corpus.Records[1].Fields;
        Assert.Equal("BancroftChardonnay", chardonnay["name"]);
        Assert.Equal("Medium", chardonnay["hasBody"]);
        Assert.Equal("NapaRegion; CaliforniaRegion", chardonnay["locatedIn"]);
        Assert.DoesNotContain(corpus.Records.SelectMany(r => r.Fields.Values), v => v is "Chardonnay" or "Region");
        Assert.Equal("1998", corpus.Records[2].Fields["yearValue"]);
    }

    [Fact]
    public void Gold_terms_past_the_ceiling_are_named_with_why()
    {
        var corpus = Cq4oeCase.FromGold("cq4oe-test", Ontology, Terms);

        Assert.Equal(CeilingReason.LiteralValue, corpus.Ceiling["yearValue"]);
        Assert.Equal(CeilingReason.HierarchyOnly, corpus.Ceiling["Wine"]);
        Assert.False(corpus.Ceiling.ContainsKey("Region"));
        Assert.False(corpus.Ceiling.ContainsKey("locatedIn"));
    }

    [Fact]
    public void A_question_is_scored_on_all_its_terms_and_apart_on_those_short_of_the_ceiling()
    {
        var corpus = Cq4oeCase.FromGold("cq4oe-test", Ontology, Terms);

        Assert.Equal(["region", "wine", "locatedin|located_in|located in"], Cq4oeCase.All(corpus.Questions[0])!.RequiredConcepts);
        Assert.Equal(["region", "locatedin|located_in|located in"], corpus.Reachable(corpus.Questions[0])!.RequiredConcepts);
        Assert.Null(corpus.Reachable(corpus.Questions[1]));
    }

    [Theory]
    [InlineData("RedBordeaux", "red bordeaux")]
    [InlineData("hasVintageYear", "has vintage year")]
    [InlineData("water_body", "water body")]
    [InlineData("SAREF4WATRDevice", "saref4watr device")]
    [InlineData("YearValue", "year value")]
    public void Terms_split_into_words_at_case_and_separators(string term, string words)
        => Assert.Equal(words, string.Join(' ', Cq4oeCase.Words(term)));

    [Fact]
    public void A_term_too_short_to_match_as_a_substring_has_no_concept()
        => Assert.Null(Cq4oeCase.Concept("Dry"));
}
