using Eyu.Core.Primitives;
using Eyu.Core.Records;

namespace Eyu.Core.Ports;

/// <summary>
/// Supplies raw records for a subject, up to <paramref name="maxCount"/> — the seam a consumer
/// fills when declared structure alone is insufficient (or absent). Eyu never fetches or retries;
/// it reads only what is handed back.
/// </summary>
public interface IRecordSample
{
    Task<IReadOnlyList<RawRecord>> SampleAsync(SubjectRef subject, int maxCount, CancellationToken cancellationToken = default);
}
