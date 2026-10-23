using System.Net;
using Eyu.Core.Inference;
using Eyu.Core.Ports;
using Xunit;

namespace Eyu.Core.Tests.Inference;

public class RetryingModelClientTests
{
    [Fact]
    public async Task A_call_that_succeeds_first_time_is_not_retried()
    {
        var inner = new ScriptedModelClient(_ => new ModelResponse("ok"));
        var retries = new List<ModelCallRetry>();
        var client = new RetryingModelClient(inner, retries: 2, onRetry: retries.Add);

        var response = await client.CompleteAsync(new ModelRequest("p"), TestContext.Current.CancellationToken);

        Assert.Equal("ok", response.Text);
        Assert.Equal(1, inner.Calls);
        Assert.Empty(retries);
    }

    [Fact]
    public async Task A_dropped_stream_is_retried_and_the_retry_is_reported()
    {
        var dropped = new HttpRequestException("The connection closed while the answer was streaming");
        var inner = new ScriptedModelClient(call => call == 1 ? throw dropped : new ModelResponse("second"));
        var retries = new List<ModelCallRetry>();
        var client = new RetryingModelClient(inner, retries: 1, onRetry: retries.Add);
        var request = new ModelRequest("p");

        var response = await client.CompleteAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("second", response.Text);
        Assert.Equal(2, inner.Calls);
        var retry = Assert.Single(retries);
        Assert.Same(request, retry.Request);
        Assert.Equal(1, retry.Attempt);
        Assert.Same(dropped, retry.Failure);
    }

    [Fact]
    public async Task A_call_that_keeps_failing_throws_the_last_attempts_exception_after_the_retries()
    {
        var inner = new ScriptedModelClient(call => throw new HttpRequestException($"attempt {call}", null, HttpStatusCode.BadGateway));
        var retries = new List<ModelCallRetry>();
        var client = new RetryingModelClient(inner, retries: 2, onRetry: retries.Add);

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(() => client.CompleteAsync(new ModelRequest("p"), TestContext.Current.CancellationToken));

        Assert.Equal("attempt 3", thrown.Message);
        Assert.Equal(3, inner.Calls);
        Assert.Equal([1, 2], retries.Select(r => r.Attempt));
    }

    [Fact]
    public async Task Zero_retries_passes_the_first_failure_through()
    {
        var inner = new ScriptedModelClient(_ => throw new HttpRequestException("dropped"));
        var client = new RetryingModelClient(inner, retries: 0);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.CompleteAsync(new ModelRequest("p"), TestContext.Current.CancellationToken));

        Assert.Equal(1, inner.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task A_refusal_is_not_retried(HttpStatusCode status)
    {
        var inner = new ScriptedModelClient(_ => throw new HttpRequestException("refused", null, status));
        var client = new RetryingModelClient(inner, retries: 3);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.CompleteAsync(new ModelRequest("p"), TestContext.Current.CancellationToken));

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task An_empty_answer_is_not_retried_by_default()
    {
        var inner = new ScriptedModelClient(_ => throw new InvalidOperationException("no message content"));
        var client = new RetryingModelClient(inner, retries: 3);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync(new ModelRequest("p"), TestContext.Current.CancellationToken));

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task The_caller_decides_what_is_transient()
    {
        var inner = new ScriptedModelClient(call => call == 1 ? throw new InvalidOperationException("no message content") : new ModelResponse("ok"));
        var client = new RetryingModelClient(inner, retries: 1, isTransient: e => e is InvalidOperationException);

        var response = await client.CompleteAsync(new ModelRequest("p"), TestContext.Current.CancellationToken);

        Assert.Equal("ok", response.Text);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task The_callers_cancellation_is_not_retried()
    {
        using var cancellation = new CancellationTokenSource();
        var inner = new ScriptedModelClient(_ =>
        {
            cancellation.Cancel();
            throw new TaskCanceledException("cancelled", new TimeoutException());
        });
        var client = new RetryingModelClient(inner, retries: 3);

        await Assert.ThrowsAsync<TaskCanceledException>(() => client.CompleteAsync(new ModelRequest("p"), cancellation.Token));

        Assert.Equal(1, inner.Calls);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    public void Http_failures_are_transient_by_status(HttpStatusCode? status, bool transient) =>
        Assert.Equal(transient, RetryingModelClient.IsTransient(new HttpRequestException("x", null, status)));

    [Fact]
    public void A_client_timeout_is_transient_and_a_plain_cancellation_is_not()
    {
        Assert.True(RetryingModelClient.IsTransient(new TaskCanceledException("timed out", new TimeoutException())));
        Assert.False(RetryingModelClient.IsTransient(new TaskCanceledException("cancelled")));
        Assert.False(RetryingModelClient.IsTransient(new FormatException("not JSON")));
    }

    [Fact]
    public void Negative_retries_or_delay_are_refused()
    {
        var inner = new ScriptedModelClient(_ => new ModelResponse("ok"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RetryingModelClient(inner, retries: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RetryingModelClient(inner, retries: 1, delay: TimeSpan.FromSeconds(-1)));
    }

    /// <summary>A model whose answer to the n-th call (1-based) is whatever the script returns or throws.</summary>
    private sealed class ScriptedModelClient(Func<int, ModelResponse> script) : IModelClient
    {
        public int Calls { get; private set; }

        public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(script(Calls));
        }
    }
}
