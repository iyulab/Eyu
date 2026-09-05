namespace Eyu.Core.Linkage;

/// <summary>
/// Winkler's 1990 common-prefix extension of the Jaro string metric ("String Comparator Metrics
/// and Enhanced Decision Rules in the Fellegi-Sunter Model of Record Linkage") — tuned for
/// short, name-like strings (company names, addresses, identifiers) where an early character
/// match is a stronger signal than a late one. Returns a value in [0, 1], 1 meaning identical.
/// Case-sensitive and whitespace-sensitive by design — callers normalize before comparing, the
/// same division of responsibility <see cref="FieldComparator"/> already applies to its own
/// trim/case-fold step.
/// </summary>
public static class JaroWinklerSimilarity
{
    private const int MaxCommonPrefixLength = 4;
    private const double PrefixScalingFactor = 0.1;
    private const double JaroBoostThreshold = 0.7;

    public static double Compute(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (a.Length == 0 && b.Length == 0)
        {
            return 1.0;
        }

        if (a.Length == 0 || b.Length == 0)
        {
            return 0.0;
        }

        var jaro = ComputeJaro(a, b);
        if (jaro <= JaroBoostThreshold)
        {
            return jaro;
        }

        var prefixLength = CommonPrefixLength(a, b);
        return jaro + prefixLength * PrefixScalingFactor * (1.0 - jaro);
    }

    private static double ComputeJaro(string a, string b)
    {
        var matchDistance = Math.Max(0, Math.Max(a.Length, b.Length) / 2 - 1);

        var aMatched = new bool[a.Length];
        var bMatched = new bool[b.Length];
        var matches = 0;

        for (var i = 0; i < a.Length; i++)
        {
            var start = Math.Max(0, i - matchDistance);
            var end = Math.Min(b.Length - 1, i + matchDistance);

            for (var j = start; j <= end; j++)
            {
                if (bMatched[j] || a[i] != b[j])
                {
                    continue;
                }

                aMatched[i] = true;
                bMatched[j] = true;
                matches++;
                break;
            }
        }

        if (matches == 0)
        {
            return 0.0;
        }

        var transpositions = 0;
        var bIndex = 0;
        for (var i = 0; i < a.Length; i++)
        {
            if (!aMatched[i])
            {
                continue;
            }

            while (!bMatched[bIndex])
            {
                bIndex++;
            }

            if (a[i] != b[bIndex])
            {
                transpositions++;
            }

            bIndex++;
        }

        var m = (double)matches;
        return (m / a.Length + m / b.Length + (m - transpositions / 2.0) / m) / 3.0;
    }

    private static int CommonPrefixLength(string a, string b)
    {
        var max = Math.Min(MaxCommonPrefixLength, Math.Min(a.Length, b.Length));
        var i = 0;
        while (i < max && a[i] == b[i])
        {
            i++;
        }

        return i;
    }
}
