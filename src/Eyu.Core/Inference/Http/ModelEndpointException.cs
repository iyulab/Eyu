using System.Net;

namespace Eyu.Core.Inference.Http;

/// <summary>
/// A model endpoint answered outside the success range — thrown by <see cref="HttpModelClient"/>,
/// still an <see cref="HttpRequestException"/> with the <see cref="HttpRequestException.StatusCode"/>.
/// <see cref="RetryAfter"/> is how long the server asked the caller to wait before trying again (the
/// response's <c>Retry-After</c>, RFC 9110 §10.2.3, given as seconds or as a date), or null when it did
/// not say — a gateway that sheds load with 429 or 503 usually does, and
/// <see cref="RetryingModelClient"/> waits at least that long before its retry.
/// </summary>
public sealed class ModelEndpointException(string message, HttpStatusCode statusCode, TimeSpan? retryAfter = null)
    : HttpRequestException(message, inner: null, statusCode)
{
    /// <summary>The wait the server asked for before a retry, or null when it named none.</summary>
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
