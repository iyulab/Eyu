using System.Text.Json;
using Eyu.Core.Inference;
using Eyu.Core.Ports;
using Xunit;

namespace Eyu.Core.Tests.Quality;

// A recorded round replays as it was answered: the same request gets its responses back in order, and
// a request the round never asked is refused rather than answered with another's response.
public sealed class RecordedModelResponsesTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "eyu-recorded-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task A_recorded_round_replays_each_requests_responses_in_the_order_it_got_them()
    {
        var schema = JsonElement.Parse("""{"type":"object"}""");
        var recording = new RecordingModelClient(new CountingModelClient(), directory);
        var ct = TestContext.Current.CancellationToken;
        await recording.CompleteAsync(new ModelRequest("case A", schema), ct);
        await recording.CompleteAsync(new ModelRequest("case B"), ct);
        await recording.CompleteAsync(new ModelRequest("case A", schema), ct);

        var replay = new ReplayModelClient(directory);

        Assert.Equal("answer 1 to case A", (await replay.CompleteAsync(new ModelRequest("case A", schema), ct)).Text);
        Assert.Equal("answer 3 to case A", (await replay.CompleteAsync(new ModelRequest("case A", schema), ct)).Text);
        Assert.Equal("answer 2 to case B", (await replay.CompleteAsync(new ModelRequest("case B"), ct)).Text);
    }

    [Fact]
    public async Task A_request_the_round_never_asked_is_refused()
    {
        var ct = TestContext.Current.CancellationToken;
        await new RecordingModelClient(new CountingModelClient(), directory).CompleteAsync(new ModelRequest("case A"), ct);
        var replay = new ReplayModelClient(directory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => replay.CompleteAsync(new ModelRequest("case A, reworded"), ct));
        await replay.CompleteAsync(new ModelRequest("case A"), ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => replay.CompleteAsync(new ModelRequest("case A"), ct));
    }

    private sealed class CountingModelClient : IModelClient
    {
        private int calls;

        public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ModelResponse($"answer {++calls} to {request.Prompt}", Usage: new TokenUsage(1, 2, 3)));
    }
}
