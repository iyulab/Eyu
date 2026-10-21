using Eyu.Core.Primitives;
using Eyu.Core.Records;

namespace Eyu.Core.Linkage;

/// <summary>
/// The Fellegi-Sunter pre-filter's first stage: a per-field, per-record-pair agreement vector.
/// Only fields present with a non-blank value on both sides are compared — a field missing on
/// either side carries no evidence and is excluded rather than counted as a disagreement.
/// Agreement is exact-match by default — case-insensitive, ignoring leading and trailing whitespace
/// (not whitespace inside the value), and comparing the values in one Unicode form
/// (<see cref="TextForm.Fold"/>: decomposed Hangul, full-width digits); passing a
/// <see cref="LinkageOptions"/> with another <see cref="LinkageOptions.Comparison"/> — or one per field,
/// <see cref="LinkageOptions.FieldComparisons"/> — switches to a similarity threshold instead
/// (<see cref="FieldComparison"/>) — a <see cref="JaroWinklerSimilarity"/>
/// over the whole value, Hangul spelled out as its jamo, so one changed letter of a syllable is one
/// letter, not one of three characters; or a <see cref="TokenSimilarity"/> over its words, so words in
/// another order or an extra word on one side still count — and notation variants that are not
/// literal duplicates register as agreement. A comparison with several thresholds sorts each pair into a
/// level (<see cref="FieldAgreementLevel"/>) rather than agree/disagree. An exact match scores 1.0 under
/// every method and clears every threshold, so it always lands on the top level.
/// </summary>
public static class FieldComparator
{
    public static IReadOnlyDictionary<string, FieldAgreementLevel> Compare(
        RawRecord a, RawRecord b, LinkageOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        var opts = options ?? LinkageOptions.Default;
        var result = new Dictionary<string, FieldAgreementLevel>(StringComparer.Ordinal);

        foreach (var fieldName in a.Fields.Keys.Intersect(b.Fields.Keys, StringComparer.Ordinal))
        {
            var valueA = a.Fields[fieldName];
            var valueB = b.Fields[fieldName];

            if (string.IsNullOrWhiteSpace(valueA) || string.IsNullOrWhiteSpace(valueB))
            {
                continue;
            }

            var trimmedA = TextForm.Fold(valueA).Trim();
            var trimmedB = TextForm.Fold(valueB).Trim();

            result[fieldName] = opts.ComparisonFor(fieldName).Level(trimmedA, trimmedB);
        }

        return result;
    }
}
