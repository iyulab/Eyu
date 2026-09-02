using Eyu.Core.Declared;
using Eyu.Core.Proposals;
using Eyu.Core.Records;

namespace Eyu.Core.Ports;

/// <summary>
/// The core judgment: entities, relations, confidence, and entity resolution (merging records
/// that denote the same entity), from declared structure and/or sampled records. At least one of
/// <paramref name="declaredStructure"/> or a non-empty <paramref name="records"/> must be
/// supplied — see design rationale §A on why judgment always requires some prior structure.
///
/// This interface declaration does not commit to an internal implementation strategy; how the
/// judgment itself is organized (e.g. a single pass vs. multiple internal roles) is a separate,
/// benchmark-gated decision — see ROADMAP.md.
/// </summary>
public interface IOntologyProposer
{
    Task<OntologyProposal> ProposeAsync(
        DeclaredStructure? declaredStructure,
        IReadOnlyList<RawRecord> records,
        CancellationToken cancellationToken = default);
}
