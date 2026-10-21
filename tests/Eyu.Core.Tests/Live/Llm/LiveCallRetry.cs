namespace Eyu.Core.Tests.Live.Llm;

/// <summary>
/// One model call of a measurement round, tried a second time when the first attempt throws. A shared
/// gateway drops a call now and then (a stream cut short, a refused connection) and the same call goes
/// through when it runs again, so without a retry a round scores those inputs as missed and has to be
/// patched by a rerun of its failed batches. One retry, not a loop: a call that fails twice is a
/// failure the report names. A retried call is counted, so a report says its numbers include retries.
/// </summary>
internal static class LiveCallRetry
{
    /// <summary>
    /// Runs <paramref name="call"/>, and runs it once more when it throws (cancellation aside). The second
    /// attempt's exception is the one that propagates; <c>FirstFailure</c> holds the first attempt's when
    /// the retry succeeded.
    /// </summary>
    public static async Task<(T Result, Exception? FirstFailure)> RunAsync<T>(Func<Task<T>> call, CancellationToken cancellation)
    {
        try
        {
            return (await call(), null);
        }
        catch (Exception first) when (first is not OperationCanceledException || !cancellation.IsCancellationRequested)
        {
            return (await call(), first);
        }
    }
}
