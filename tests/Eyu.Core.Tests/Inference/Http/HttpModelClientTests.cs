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

        await client.CompleteAsync(new ModelRequest("hello"));

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

        var response = await client.CompleteAsync(new ModelRequest("anything"));

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

        var response = await client.CompleteAsync(new ModelRequest("anything"));

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

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync(new ModelRequest("anything")));
    }

    [Fact]
    public async Task CompleteAsync_surfaces_a_non_success_status_as_a_failure()
    {
        var handler = new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var client = new HttpModelClient(new HttpClient(handler) { BaseAddress = new Uri("https://example.test/v1/") }, "test-model");

        await Assert.ThrowsAsync<HttpRequestException>(() => client.CompleteAsync(new ModelRequest("anything")));
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
