using Eyu.Core.Inference;

namespace Eyu.Core.Ports;

/// <summary>
/// Provider-neutral inference access — a local or hosted model both satisfy this unmodified. The
/// only seam through which Eyu's judgment reaches a model; see design rationale §D for the
/// confidence-calibration obligation this carries. An implementation whose provider offers
/// structured output maps <see cref="ModelRequest.ResponseSchema"/> to it; one whose provider does
/// not may ignore the schema, and the caller's prompt and validation still apply.
/// </summary>
public interface IModelClient
{
    Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default);
}
