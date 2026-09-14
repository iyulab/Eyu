using System.Net;
using System.Text;
using System.Text.Json;
using Eyu.Core.Inference;
using Eyu.Core.Inference.Http;
using Xunit;

namespace Eyu.Core.Tests.Inference.Http;

public class HttpModelClientTests
{
    [Fact]
    public async Task CompleteAsync_sends_the_prompt_as_a_single_user_message()
    {
        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        var handler = new FakeHandler(async request =>
        {
            captured = request;
            capturedBody = request.Content is null ? null : await request.Content.ReadAsStringAsync();
            return CannedResponse("hello back");
        });
        var client = new HttpModelClient(new HttpClient(handler) { BaseAddress = new Uri("https://example.test/v1/") }, "test-model");

        await client.CompleteAsync(new ModelRequest("hello"), TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal(new Uri("https://example.test/v1/chat/completions"), captured.RequestUri);

        using var payload = JsonDocument.Parse(capturedBody!);
        Assert.Equal("test-model", payload.RootElement.GetProperty("model").GetString());
        var message = payload.RootElement.GetProperty("messages")[0];
        Assert.Equal("user", message.GetProperty("role").GetString());
        Assert.Equal("hello", message.GetProperty("content").GetString());
    }

    [Fact]
    public async Task CompleteAsync_returns_the_first_choices_message_content()
    {
        var handler = new FakeHandler(_ => Task.FromResult(CannedResponse("the answer")));
        var client = new HttpModelClient(new HttpClient(handler) { BaseAddress = new Uri("https://example.test/v1/") }, "test-model");

        var response = await client.CompleteAsync(new ModelRequest("anything"), TestContext.Current.CancellationToken);

        Assert.Equal("the answer", response.Text);
    }

    [Fact]
    public async Task CompleteAsync_leaves_confidence_unset_when_the_response_carries_none()
    {
        // OpenAI-compatible chat completions do not report a confidence score -- this
        // implementation never invents one (design rationale SS D: uncalibrated is worse than
        // absent, and nothing here is calibrated to report as a number).
        var handler = new FakeHandler(_ => Task.FromResult(CannedResponse("x")));
        var client = new HttpModelClient(new HttpClient(handler) { BaseAddress = new Uri("https://example.test/v1/") }, "test-model");

        var response = await client.CompleteAsync(new ModelRequest("anything"), TestContext.Current.CancellationToken);

        Assert.Null(response.Confidence);
    }

    [Fact]
    public async Task CompleteAsync_throws_when_the_response_carries_no_choices()
    {
        var handler = new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"choices":[]}""", Encoding.UTF8, "application/json"),
        }));
        var client = new HttpModelClient(new HttpClient(handler) { BaseAddress = new Uri("https://example.test/v1/") }, "test-model");

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync(new ModelRequest("anything"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CompleteAsync_reports_what_the_provider_actually_returned()
    {
        // A 2xx with no usable content can be a refusal, a filtered completion, or a payload in a
        // shape this client does not know. The body is already read at the throw site, so the
        // exception carries it instead of making the caller re-run with logging to find out.
        var handler = new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"error":{"message":"content filtered","code":"content_filter"}}""",
                Encoding.UTF8,
                "application/json"),
        }));
        var client = new HttpModelClient(new HttpClient(handler) { BaseAddress = new Uri("https://example.test/v1/") }, "test-model");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.CompleteAsync(new ModelRequest("anything"), TestContext.Current.CancellationToken));

        Assert.Contains("content_filter", error.Message);
    }

    [Fact]
    public async Task CompleteAsync_bounds_the_reported_response_body()
    {
        var handler = new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{"choices":[],"padding":"{{new string('x', 4000)}}"}""",
                Encoding.UTF8,
                "application/json"),
        }));
        var client = new HttpModelClient(new HttpClient(handler) { BaseAddress = new Uri("https://example.test/v1/") }, "test-model");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.CompleteAsync(new ModelRequest("anything"), TestContext.Current.CancellationToken));

        Assert.True(error.Message.Length < 1000, $"message was {error.Message.Length} chars");
        Assert.Contains("chars total", error.Message);
    }

    [Fact]
    public async Task CompleteAsync_surfaces_a_non_success_status_as_a_failure()
    {
        var handler = new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var client = new HttpModelClient(new HttpClient(handler) { BaseAddress = new Uri("https://example.test/v1/") }, "test-model");

        await Assert.ThrowsAsync<HttpRequestException>(() => client.CompleteAsync(new ModelRequest("anything"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CompleteAsync_merges_opaque_extra_body_fields_into_the_request()
    {
        // Provider-specific knobs a consumer needs (a self-hosted server's thinking control, a
        // temperature) ride as opaque JSON the client does not interpret — the OpenAI SDK's
        // extra_body convention, kept provider-neutral (design: no domain field earns a named slot).
        string? capturedBody = null;
        var handler = new FakeHandler(async request =>
        {
            capturedBody = request.Content is null ? null : await request.Content.ReadAsStringAsync();
            return CannedResponse("ok");
        });
        var extraBody = new Dictionary<string, JsonElement>
        {
            ["temperature"] = JsonSerializer.SerializeToElement(0.2),
            ["chat_template_kwargs"] = JsonSerializer.SerializeToElement(new { enable_thinking = false }),
        };
        var client = new HttpModelClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://example.test/v1/") }, "test-model", extraBody);

        await client.CompleteAsync(new ModelRequest("hi"), TestContext.Current.CancellationToken);

        using var payload = JsonDocument.Parse(capturedBody!);
        Assert.Equal("test-model", payload.RootElement.GetProperty("model").GetString());
        Assert.Equal("hi", payload.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.Equal(0.2, payload.RootElement.GetProperty("temperature").GetDouble());
        Assert.False(payload.RootElement.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean());
    }

    [Fact]
    public async Task CompleteAsync_does_not_let_extra_body_override_model_or_messages()
    {
        // The client owns model and messages; an extra-body entry with either key is ignored rather
        // than silently breaking the request it is layered onto.
        string? capturedBody = null;
        var handler = new FakeHandler(async request =>
        {
            capturedBody = request.Content is null ? null : await request.Content.ReadAsStringAsync();
            return CannedResponse("ok");
        });
        var extraBody = new Dictionary<string, JsonElement>
        {
            ["model"] = JsonSerializer.SerializeToElement("hijacked"),
            ["messages"] = JsonSerializer.SerializeToElement("hijacked"),
        };
        var client = new HttpModelClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://example.test/v1/") }, "test-model", extraBody);

        await client.CompleteAsync(new ModelRequest("hi"), TestContext.Current.CancellationToken);

        using var payload = JsonDocument.Parse(capturedBody!);
        Assert.Equal("test-model", payload.RootElement.GetProperty("model").GetString());
        Assert.Equal(JsonValueKind.Array, payload.RootElement.GetProperty("messages").ValueKind);
    }

    [Fact]
    public async Task CompleteAsync_maps_a_response_schema_to_structured_output()
    {
        // A model given only a prose description of the JSON it should return can still wrap it in
        // a markdown fence; the request's schema goes out as OpenAI structured output so a server
        // that supports it holds the answer to bare JSON.
        string? capturedBody = null;
        var handler = new FakeHandler(async request =>
        {
            capturedBody = request.Content is null ? null : await request.Content.ReadAsStringAsync();
            return CannedResponse("{}");
        });
        var client = new HttpModelClient(new HttpClient(handler) { BaseAddress = new Uri("https://example.test/v1/") }, "test-model");
        var schema = JsonElement.Parse("""{"type":"object","properties":{"answer":{"type":"string"}},"required":["answer"],"additionalProperties":false}""");

        await client.CompleteAsync(new ModelRequest("hi", schema), TestContext.Current.CancellationToken);

        using var payload = JsonDocument.Parse(capturedBody!);
        var responseFormat = payload.RootElement.GetProperty("response_format");
        Assert.Equal("json_schema", responseFormat.GetProperty("type").GetString());
        var jsonSchema = responseFormat.GetProperty("json_schema");
        Assert.False(string.IsNullOrEmpty(jsonSchema.GetProperty("name").GetString()));
        Assert.True(jsonSchema.GetProperty("strict").GetBoolean());
        Assert.True(JsonElement.DeepEquals(schema, jsonSchema.GetProperty("schema")));
    }

    [Fact]
    public async Task CompleteAsync_sends_no_response_format_for_a_request_without_a_schema()
    {
        string? capturedBody = null;
        var handler = new FakeHandler(async request =>
        {
            capturedBody = request.Content is null ? null : await request.Content.ReadAsStringAsync();
            return CannedResponse("ok");
        });
        var client = new HttpModelClient(new HttpClient(handler) { BaseAddress = new Uri("https://example.test/v1/") }, "test-model");

        await client.CompleteAsync(new ModelRequest("hi"), TestContext.Current.CancellationToken);

        using var payload = JsonDocument.Parse(capturedBody!);
        Assert.False(payload.RootElement.TryGetProperty("response_format", out _));
    }

    [Fact]
    public async Task CompleteAsync_lets_an_extra_body_response_format_replace_the_mapped_one()
    {
        // Servers differ in which structured-output forms they honour or reject; the caller knows its
        // server, so its own response_format (here, turning structured output off) wins.
        string? capturedBody = null;
        var handler = new FakeHandler(async request =>
        {
            capturedBody = request.Content is null ? null : await request.Content.ReadAsStringAsync();
            return CannedResponse("{}");
        });
        var extraBody = new Dictionary<string, JsonElement>
        {
            ["response_format"] = JsonSerializer.SerializeToElement(new { type = "text" }),
        };
        var client = new HttpModelClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://example.test/v1/") }, "test-model", extraBody);
        var schema = JsonElement.Parse("""{"type":"object"}""");

        await client.CompleteAsync(new ModelRequest("hi", schema), TestContext.Current.CancellationToken);

        using var payload = JsonDocument.Parse(capturedBody!);
        var responseFormat = payload.RootElement.GetProperty("response_format");
        Assert.Equal("text", responseFormat.GetProperty("type").GetString());
        Assert.False(responseFormat.TryGetProperty("json_schema", out _));
    }

    [Fact]
    public async Task CompleteAsync_passes_through_token_usage_when_the_response_reports_it()
    {
        var handler = new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"choices":[{"message":{"content":"ok"}}],"usage":{"prompt_tokens":11,"completion_tokens":7,"total_tokens":18}}""",
                Encoding.UTF8,
                "application/json"),
        }));
        var client = new HttpModelClient(new HttpClient(handler) { BaseAddress = new Uri("https://example.test/v1/") }, "test-model");

        var response = await client.CompleteAsync(new ModelRequest("hi"), TestContext.Current.CancellationToken);

        Assert.NotNull(response.Usage);
        Assert.Equal(11, response.Usage!.PromptTokens);
        Assert.Equal(7, response.Usage.CompletionTokens);
        Assert.Equal(18, response.Usage.TotalTokens);
    }

    [Fact]
    public async Task CompleteAsync_leaves_usage_unset_when_the_response_omits_it()
    {
        var handler = new FakeHandler(_ => Task.FromResult(CannedResponse("ok")));
        var client = new HttpModelClient(new HttpClient(handler) { BaseAddress = new Uri("https://example.test/v1/") }, "test-model");

        var response = await client.CompleteAsync(new ModelRequest("hi"), TestContext.Current.CancellationToken);

        Assert.Null(response.Usage);
    }

    private static HttpResponseMessage CannedResponse(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } }),
            Encoding.UTF8,
            "application/json"),
    };

    private sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => respond(request);
    }
}
