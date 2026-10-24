using System.Net;
using Eyu.Core.Inference.Http;
using Eyu.Core.Ports;

namespace Eyu.Core.Inference;

/// <summary>
/// An <see cref="IModelClient"/> that tries a call again when it fails transiently — opt-in, wrapped
/// around another client: <c>new RetryingModelClient(new HttpModelClient(http, model), retries: 1)</c>.
/// <see cref="HttpModelClient"/> itself never retries. A retry policy on the <see cref="HttpClient"/>'s
/// handler pipeline covers what fails before the response arrives, but a streamed answer cut in the
/// middle surfaces from <see cref="IModelClient.CompleteAsync"/> after that pipeline is done; wrapping the
/// call covers both.
///
/// <paramref name="retries"/> is how many more times a failed call is tried — the caller's choice, since
/// a call to a thinking model can take minutes and only the caller knows what a repeat costs. A call that
/// still fails after the last retry throws that attempt's exception. A cancellation requested through the
/// caller's token is never retried.
///
/// Which failures are retried is <paramref name="isTransient"/>, by default <see cref="IsTransient"/>:
/// a dropped connection or stream, an error the endpoint reports inside a streamed answer, a request
/// timeout, and the statuses that ask for a retry (408, 429, 5xx). A refusal (any other 4xx), a response
/// with no content, and an answer the proposer cannot parse are not — sending the same request again
/// would mostly get the same answer.
///
/// Every retry is reported to <paramref name="onRetry"/> before it runs, with the request, the attempt
/// that failed and its exception, so a caller can say its results include retried calls (a review
/// document's «retried 1», a benchmark's retried-call count) instead of reporting them as first-try
/// answers. <paramref name="delay"/> is waited before the first retry and doubled before each further
/// one; zero (the default) retries at once. When the failure says how long the endpoint asked to be left
/// alone (<see cref="ModelEndpointException.RetryAfter"/> — a load-shedding gateway's 429 or 503), the
/// retry waits at least that long: asking again sooner is only shed again.
/// </summary>
public sealed class RetryingModelClient(
    IModelClient inner,
    int retries,
    Action<ModelCallRetry>? onRetry = null,
    Func<Exception, bool>? isTransient = null,
    TimeSpan delay = default) : IModelClient
{
    private readonly IModelClient _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly int _retries = retries >= 0 ? retries : throw new ArgumentOutOfRangeException(nameof(retries), retries, "Retries must not be negative.");
    private readonly Func<Exception, bool> _isTransient = isTransient ?? IsTransient;
    private readonly TimeSpan _delay = delay >= TimeSpan.Zero ? delay : throw new ArgumentOutOfRangeException(nameof(delay), delay, "Delay must not be negative.");

    public async Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await _inner.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception failure) when (attempt <= _retries && !cancellationToken.IsCancellationRequested && _isTransient(failure))
            {
                onRetry?.Invoke(new ModelCallRetry(request, attempt, failure));
                var wait = _delay * Math.Pow(2, attempt - 1);
                if (failure is ModelEndpointException { RetryAfter: { } asked } && asked > wait)
                {
                    wait = asked;
                }

                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>
    /// The default transient test: an <see cref="HttpRequestException"/> with no status (the connection or
    /// the stream dropped, or the endpoint reported an error inside the stream) or with 408, 429 or a 5xx
    /// status, and a <see cref="TaskCanceledException"/> caused by a <see cref="TimeoutException"/> (the
    /// <see cref="HttpClient"/> timeout). The caller's own cancellation is excluded before this is asked.
    /// </summary>
    public static bool IsTransient(Exception exception) => exception switch
    {
        HttpRequestException { StatusCode: null } => true,
        HttpRequestException { StatusCode: { } status } => status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)status >= 500,
        TaskCanceledException { InnerException: TimeoutException } => true,
        _ => false,
    };
}

/// <summary>
/// One retry <see cref="RetryingModelClient"/> is about to make: the <paramref name="Request"/>, the
/// <paramref name="Attempt"/> that failed (1 for the first call) and that attempt's <paramref name="Failure"/>.
/// </summary>
public sealed record ModelCallRetry(ModelRequest Request, int Attempt, Exception Failure);
