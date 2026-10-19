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
/// <see cref="LinkageOptions"/> with <see cref="LinkageOptions.UseStringSimilarityComparator"/>
/// set switches to a <see cref="JaroWinklerSimilarity"/> threshold instead — over Hangul spelled out
/// as its jamo, so one changed letter of a syllable is one letter, not one of three characters — so notation variants
/// (punctuation, spacing) that are not literal duplicates still register as
/// <see cref="FieldAgreementLevel.Agree"/> — an exact match always scores 1.0 and clears any
/// reasonable threshold, so this mode is a strict superset of the default's exact-match cases.
/// </summary>
public static class FieldComparator
{
    public const double DefaultStringSimilarityAgreementThreshold = 0.90;

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

            var agrees = opts.UseStringSimilarityComparator
                ? JaroWinklerSimilarity.Compute(TextForm.DecomposeHangul(trimmedA.ToUpperInvariant()), TextForm.DecomposeHangul(trimmedB.ToUpperInvariant()))
                    >= opts.StringSimilarityAgreementThreshold
                : string.Equals(trimmedA, trimmedB, StringComparison.OrdinalIgnoreCase);

            result[fieldName] = agrees ? FieldAgreementLevel.Agree : FieldAgreementLevel.Disagree;
        }

        return result;
    }
}
