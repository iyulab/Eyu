using Eyu.Core.Inference;
using Eyu.Core.Ports;

namespace Eyu.Core.Tests;

/// <summary>A model that answers every request with the same text and keeps the last request it was sent.</summary>
internal sealed class StubModelClient(string responseText) : IModelClient
{
    public string? LastPrompt => LastRequest?.Prompt;

    public ModelRequest? LastRequest { get; private set; }

    public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult(new ModelResponse(responseText));
    }
}
