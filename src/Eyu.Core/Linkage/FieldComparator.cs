using Eyu.Core.Records;

namespace Eyu.Core.Linkage;

/// <summary>
/// The Fellegi-Sunter pre-filter's first stage: a per-field, per-record-pair agreement vector.
/// Only fields present with a non-blank value on both sides are compared — a field missing on
/// either side carries no evidence and is excluded rather than counted as a disagreement.
/// </summary>
public static class FieldComparator
{
    public static IReadOnlyDictionary<string, FieldAgreementLevel> Compare(RawRecord a, RawRecord b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        var result = new Dictionary<string, FieldAgreementLevel>(StringComparer.Ordinal);

        foreach (var fieldName in a.Fields.Keys.Intersect(b.Fields.Keys, StringComparer.Ordinal))
        {
            var valueA = a.Fields[fieldName];
            var valueB = b.Fields[fieldName];

            if (string.IsNullOrWhiteSpace(valueA) || string.IsNullOrWhiteSpace(valueB))
            {
                continue;
            }

            var agrees = string.Equals(valueA.Trim(), valueB.Trim(), StringComparison.OrdinalIgnoreCase);
            result[fieldName] = agrees ? FieldAgreementLevel.Agree : FieldAgreementLevel.Disagree;
        }

        return result;
    }
}
