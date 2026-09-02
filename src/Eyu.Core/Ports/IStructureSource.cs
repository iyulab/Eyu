using Eyu.Core.Declared;
using Eyu.Core.Primitives;

namespace Eyu.Core.Ports;

/// <summary>
/// Supplies what a caller has already declared for a subject — field hints, relations, version —
/// or null when nothing is declared. The seam a consumer fills for its own world (a declared
/// schema, an M3L-style declaration, a federated read); Eyu never fetches this itself.
/// </summary>
public interface IStructureSource
{
    Task<DeclaredStructure?> GetStructureAsync(SubjectRef subject, CancellationToken cancellationToken = default);
}
