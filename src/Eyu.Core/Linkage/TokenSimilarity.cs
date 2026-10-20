using Eyu.Core.Primitives;

namespace Eyu.Core.Linkage;

/// <summary>
/// Similarity of two values as bags of words — the token-based comparisons record linkage uses where
/// one value's words reappear in the other in a different order, or with words added on one side.
/// A token is a maximal run of letters and digits (anything else separates), compared as it is: like
/// <see cref="JaroWinklerSimilarity"/>, these methods leave case and Unicode form to the caller.
/// Both return a value in [0, 1], 1 meaning the same words.
/// </summary>
public static class TokenSimilarity
{
    /// <summary>
    /// The words of <paramref name="text"/>: maximal runs of letters, digits and combining marks, in
    /// order, duplicates kept.
    /// </summary>
    public static IReadOnlyList<string> Tokenize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var tokens = new List<string>();
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var inToken = i < text.Length && IsTokenChar(text[i]);
            if (inToken && start < 0)
            {
                start = i;
            }
            else if (!inToken && start >= 0)
            {
                tokens.Add(text[start..i]);
                start = -1;
            }
        }

        return tokens;
    }

    /// <summary>
    /// The Jaccard coefficient of the two values' sets of words: words in both over words in either.
    /// Two values with no words at all are the same (1); one with words and one without share none (0).
    /// </summary>
    public static double Jaccard(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        var setA = Tokenize(a).ToHashSet(StringComparer.Ordinal);
        var setB = Tokenize(b).ToHashSet(StringComparer.Ordinal);
        if (setA.Count == 0 && setB.Count == 0)
        {
            return 1.0;
        }

        var shared = setA.Count(setB.Contains);
        return (double)shared / (setA.Count + setB.Count - shared);
    }

    /// <summary>
    /// Monge and Elkan's 1996 field-matching score, made symmetric: for each word of one value, the
    /// <see cref="JaroWinklerSimilarity"/> of its closest word in the other (Hangul spelled out as its
    /// jamo, as <see cref="FieldComparison.JaroWinkler"/> does), averaged over that value's words — then
    /// the mean of the two directions. The one-directional score is asymmetric (a short value inside a
    /// long one scores high one way only); linkage compares unordered pairs, so both directions count.
    /// Two values with no words at all are the same (1); one with words and one without share none (0).
    /// </summary>
    public static double MongeElkan(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        var tokensA = Tokenize(a).Select(TextForm.DecomposeHangul).ToList();
        var tokensB = Tokenize(b).Select(TextForm.DecomposeHangul).ToList();
        if (tokensA.Count == 0 && tokensB.Count == 0)
        {
            return 1.0;
        }

        if (tokensA.Count == 0 || tokensB.Count == 0)
        {
            return 0.0;
        }

        return (Directed(tokensA, tokensB) + Directed(tokensB, tokensA)) / 2.0;
    }

    private static double Directed(List<string> from, List<string> to) =>
        from.Average(token => to.Max(other => JaroWinklerSimilarity.Compute(token, other)));

    private static bool IsTokenChar(char c) =>
        char.IsLetterOrDigit(c) || char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.NonSpacingMark
            or System.Globalization.UnicodeCategory.SpacingCombiningMark;
}
