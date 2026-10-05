using System.Net;
using System.Text;
using System.Text.Json;
using Eyu.Core.Declared;
using Eyu.Core.Inference.Http;
using Eyu.Core.Judgment;
using Eyu.Core.Linkage;
using Eyu.Core.Primitives;
using Eyu.Core.Records;
using Eyu.Core.Routing;
using Eyu.Formbase;
using Eyu.Rdf;
using Formbase.Core.InMemory;
using Formbase.Core.Primitives;
using Formbase.Core.Schema;

// Every public path that serializes, end to end, with no model behind it: the HTTP client's request
// and response, the proposer's parse, routing's provenance annotations, the Turtle export, and the
// Formbase adapter. Under Native AOT a path that still needs reflection throws here, so the process
// exits non-zero and the publish-and-run step fails.

var records = new List<RawRecord>
{
    new("r-01", new Dictionary<string, string?> { ["name"] = "김민수", ["city"] = "서울" }),
    new("r-02", new Dictionary<string, string?> { ["name"] = "김민수", ["city"] = "서울" }),
    new("r-03", new Dictionary<string, string?> { ["name"] = "이영희", ["city"] = "부산" }),
};

var linkage = LinkagePipeline.Analyze(records);
Check(linkage.PairLinkages.Count == 3, $"linkage pairs: {linkage.PairLinkages.Count}");

const string answer = """
    {"entities": [
      {"id": "p1", "name": "김민수", "type": "Person", "claim": "two records of one person", "sources": ["r-01", "r-02"], "denotedBy": ["r-01", "r-02"], "confidence": 0.9},
      {"id": "p2", "name": "이영희", "type": "Person", "claim": "a person", "sources": ["r-03"], "denotedBy": ["r-03"], "confidence": 0.8}
    ], "relations": []}
    """;
var handler = new ChatCompletionsStub(answer);
using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/v1/") };
var extraBody = new Dictionary<string, JsonElement> { ["temperature"] = JsonElement.Parse("0") };
var client = new HttpModelClient(http, "stub-model", extraBody);

var proposal = await new SinglePassOntologyProposer(client).ProposeAsync([], records);
Check(proposal.Entities.Count == 2, $"entities: {proposal.Entities.Count}");
Check(proposal.Rejections.Count == 0, $"rejections: {proposal.Rejections.Count}");

using (var sent = JsonDocument.Parse(handler.LastRequestBody ?? "{}"))
{
    var root = sent.RootElement;
    Check(root.GetProperty("model").GetString() == "stub-model", "request model");
    Check(root.GetProperty("messages")[0].GetProperty("role").GetString() == "user", "request message role");
    Check(root.GetProperty("response_format").GetProperty("json_schema").GetProperty("schema").ValueKind == JsonValueKind.Object, "request schema");
    Check(root.GetProperty("temperature").GetInt32() == 0, "request extra body");
}

var traced = proposal.Trace(RoutingPolicy.Uniform(new RoutingThresholds(AutoApplyAt: 0.95, ReviewAt: 0.5)));
var sources = traced.Entities[0].Provenance.Annotations![ProvenanceAnnotations.Sources];
Check(sources == """["r-01","r-02"]""", $"sources annotation: {sources}");

var turtle = OntologyTurtle.ToTurtle(proposal, new RdfExportOptions(new Uri("https://example.org/smoke/")));
Check(turtle.Contains("owl:NamedIndividual", StringComparison.Ordinal), "turtle individuals");

var hints = new InMemoryFieldHintSource();
hints.Declare(new FormTypeHints(
    FormTypeRef.Create("person"),
    TableName: "person",
    Fields: [new FieldHint("name", ColumnType.Text, Nullable: false), new FieldHint("city", ColumnType.Text)]));
var structure = await new FormbaseStructureSource(hints).GetStructureAsync(SubjectRef.Create("person"));
Check(structure is { Fields.Count: 2 }, "formbase structure");

Console.WriteLine("Eyu AOT smoke: ok");
return 0;

static void Check(bool condition, string what)
{
    if (!condition)
    {
        throw new InvalidOperationException($"AOT smoke check failed: {what}");
    }
}

internal sealed class ChatCompletionsStub(string content) : HttpMessageHandler
{
    public string? LastRequestBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var body = new System.Text.Json.Nodes.JsonObject
        {
            ["choices"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject
            {
                ["message"] = new System.Text.Json.Nodes.JsonObject { ["content"] = content },
            }),
            ["usage"] = new System.Text.Json.Nodes.JsonObject { ["prompt_tokens"] = 1, ["completion_tokens"] = 1, ["total_tokens"] = 2 },
        };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
    }
}
