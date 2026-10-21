using Eyu.Core.Linkage;

namespace Eyu.Core.Tests.Linkage;

/// <summary>
/// Fixtures written as classic agreement probabilities — one <c>m</c> or <c>u</c> per field — as the
/// two-level distributions <c>[1 − p, p]</c> the parameters hold, and back.
/// </summary>
internal static class BinaryLevels
{
    public static IReadOnlyDictionary<string, IReadOnlyList<double>> Of(IReadOnlyDictionary<string, double> agreement) =>
        agreement.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<double>)[1.0 - kv.Value, kv.Value], StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, IReadOnlyList<double>> None { get; } = new Dictionary<string, IReadOnlyList<double>>();
}
