using Eyu.Core.Records;

namespace Eyu.Core.Linkage;

/// <summary>
/// The Fellegi-Sunter pre-filter's first stage: a per-field, per-record-pair agreement vector.
/// Only fields present with a non-blank value on both sides are compared — a field missing on
/// either side carries no evidence and is excluded rather than counted as a disagreement.
/// Agreement is exact-match (case/whitespace-insensitive) by default; passing a
/// <see cref="LinkageOptions"/> with <see cref="LinkageOptions.UseStringSimilarityComparator"/>
/// set switches to a <see cref="JaroWinklerSimilarity"/> threshold instead, so notation variants
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

            var trimmedA = valueA.Trim();
            var trimmedB = valueB.Trim();

            var agrees = opts.UseStringSimilarityComparator
                ? JaroWinklerSimilarity.Compute(trimmedA.ToUpperInvariant(), trimmedB.ToUpperInvariant())
                    >= opts.StringSimilarityAgreementThreshold
                : string.Equals(trimmedA, trimmedB, StringComparison.OrdinalIgnoreCase);

            result[fieldName] = agrees ? FieldAgreementLevel.Agree : FieldAgreementLevel.Disagree;
        }

        return result;
    }
}
