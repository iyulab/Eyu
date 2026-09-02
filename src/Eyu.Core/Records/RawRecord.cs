namespace Eyu.Core.Records;

/// <summary>
/// A single raw record sampled from a caller's source, keyed by <paramref name="Id"/> so a later
/// grounded claim (<see cref="Eyu.Core.Grounding.SourceRef"/>) can cite exactly which record it
/// came from. <paramref name="Fields"/> is an opaque string bag — Eyu does not assume a type
/// system here; that judgment belongs to <c>IOntologyProposer</c>, not to the sampling seam.
/// </summary>
public sealed record RawRecord(string Id, IReadOnlyDictionary<string, string?> Fields);
