using Eyu.Core.Judgment;
using Eyu.Core.Records;
using Eyu.Core.Tests.Live.Llm;
using VDS.RDF;
using VDS.RDF.Parsing;
using Xunit;

namespace Eyu.Rdf.Tests.Live.Llm;

/// <summary>
/// The unit tests feed the writer names chosen to be hard; this feeds it the names a model actually
/// chooses — Korean types, relation names in whatever casing it picked, claims that restate records
/// at length, several citations per claim — and asks the same question: does an independent parser
/// read back every entity and every relation.
/// </summary>
public class OntologyTurtleLiveTests(ITestOutputHelper output)
{
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private const string Owl = "http://www.w3.org/2002/07/owl#";

    private static readonly RawRecord[] SupplierRecords =
    [
        new("s-01", new Dictionary<string, string?>
        {
            ["회사명"] = "(주)한빛정밀",
            ["대표자"] = "박서연",
            ["소재지"] = "경기도 안산시 단원구",
            ["업종"] = "금형 가공",
            ["주요 납품처"] = "대성모터스, 한결전자",
        }),
        new("s-02", new Dictionary<string, string?>
        {
            ["회사명"] = "대성모터스",
            ["대표자"] = "이정훈",
            ["소재지"] = "충청남도 아산시",
            ["업종"] = "자동차 부품 조립",
            ["비고"] = "\"1차 협력사\" 등록 — 2024년 3월",
        }),
        new("s-03", new Dictionary<string, string?>
        {
            ["회사명"] = "한결전자",
            ["대표자"] = "박서연",
            ["소재지"] = "경기도 안산시 단원구",
            ["업종"] = "전장 모듈",
        }),
    ];

    [Fact]
    public async Task A_real_models_proposal_exports_to_turtle_an_independent_parser_reads_whole()
    {
        var (httpClient, modelClient) = EyuLlmLiveClient.Create();
        using var _ = httpClient;
        var proposal = await new SinglePassOntologyProposer(modelClient)
            .ProposeAsync(declaredStructures: [], SupplierRecords, TestContext.Current.CancellationToken);

        var turtle = OntologyTurtle.ToTurtle(proposal, new RdfExportOptions(new Uri("https://example.org/suppliers#")));
        output.WriteLine(turtle);

        var g = new Graph();
        new TurtleParser().Load(g, new StringReader(turtle));

        Assert.NotEmpty(proposal.Entities);
        Assert.Equal(proposal.Entities.Count,
            g.GetTriplesWithPredicateObject(g.CreateUriNode(new Uri(Rdf + "type")), g.CreateUriNode(new Uri(Owl + "NamedIndividual"))).Count());
        Assert.Equal(proposal.Relations.Count,
            g.GetTriplesWithPredicateObject(g.CreateUriNode(new Uri(Rdf + "type")), g.CreateUriNode(new Uri(Owl + "Axiom"))).Count());
    }
}
