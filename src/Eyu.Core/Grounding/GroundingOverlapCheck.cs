using System.Text.RegularExpressions;
using Eyu.Core.Records;

namespace Eyu.Core.Grounding;

/// <summary>
/// Closes a gap <see cref="IGroundingContract"/> does not itself close: a cited
/// <see cref="SourceRef.RecordId"/> proves only that the id exists among the records a proposer
/// was given — it says nothing about whether the claim text is actually substantiated by that
/// record's content, so a source cited for the wrong reason passes the contract's own shape
/// check silently. This is a textual-overlap heuristic, not a semantic
/// verifier — it flags a claim whose cited sources share too little vocabulary with the claim,
/// which catches a mismatched citation without a human or a second model call in the loop.
/// <para>
/// Text is compared as tokens. Scripts that separate words with spaces (Latin and the like) give
/// word tokens. Chinese, Japanese and Korean text gives overlapping two-character tokens instead —
/// the standard treatment for scripts where a word does not stand apart (search engines' CJK bigram
/// filters do the same): a Korean noun carries its particle with it (<c>회사는</c>, <c>회사의</c>),
/// so whole-word matching would find almost nothing in common between a claim and the record it
/// quotes, while the noun's characters are shared either way. No dictionary is involved.
/// </para>
/// </summary>
public static partial class GroundingOverlapCheck
{
    public const double DefaultMinOverlapRatio = 0.3;

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "is", "are", "was", "were", "to", "of", "in", "on", "at", "for", "and",
        "or", "with", "by", "this", "that", "it", "as", "be", "has", "have", "had", "from", "its",
    };

    /// <summary>
    /// True when at least <paramref name="minOverlapRatio"/> of <paramref name="claim"/>'s
    /// significant tokens also appear in the content of the records its
    /// <see cref="IGroundingContract.Sources"/> cite. A significant token is a word of at least three
    /// characters that is not a stop word, or a pair of adjacent characters in Chinese, Japanese or
    /// Korean text. A claim with no significant token at all is not supported: there is nothing in it
    /// this check could find in a source. A source whose
    /// <see cref="SourceRef.FieldName"/> is set is checked against only that field; an id absent
    /// from <paramref name="records"/> contributes no supporting content.
    /// </summary>
    public static bool IsSupportedBy(IGroundingContract claim, IReadOnlyList<RawRecord> records, double minOverlapRatio = DefaultMinOverlapRatio) =>
        Evaluate(claim, records, minOverlapRatio).IsSupported;

    /// <summary>
    /// Same check as <see cref="IsSupportedBy"/>, but returns the ratio and token counts behind
    /// the verdict — a bare pass/fail cannot tell a genuinely mismatched citation apart from a
    /// correct claim whose framing language happened to dilute the ratio (this is a heuristic,
    /// not a semantic verifier; see the type doc comment), and a caller reporting results (e.g. a
    /// quality-measurement harness) needs that detail to tell the two apart.
    /// </summary>
    public static OverlapResult Evaluate(IGroundingContract claim, IReadOnlyList<RawRecord> records, double minOverlapRatio = DefaultMinOverlapRatio)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(records);

        var claimTokens = Tokenize(claim.Claim);
        if (claimTokens.Count == 0)
        {
            return new OverlapResult(IsSupported: false, Ratio: 0.0, MatchedTokenCount: 0, ClaimTokenCount: 0);
        }

        var recordsById = records.ToDictionary(r => r.Id, r => r, StringComparer.Ordinal);
        var sourceTokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in claim.Sources)
        {
            if (!recordsById.TryGetValue(source.RecordId, out var record))
            {
                continue;
            }

            IEnumerable<string?> fieldValues = source.FieldName is null
                ? record.Fields.Values
                : record.Fields.TryGetValue(source.FieldName, out var value) ? [value] : [];

            foreach (var fieldValue in fieldValues)
            {
                if (fieldValue is null)
                {
                    continue;
                }

                foreach (var token in Tokenize(fieldValue))
                {
                    sourceTokens.Add(token);
                }
            }
        }

        var matched = sourceTokens.Count == 0 ? 0 : claimTokens.Count(sourceTokens.Contains);
        var ratio = (double)matched / claimTokens.Count;
        return new OverlapResult(ratio >= minOverlapRatio, ratio, matched, claimTokens.Count);
    }

    /// <param name="IsSupported">Whether <see cref="Ratio"/> met the configured minimum.</param>
    /// <param name="Ratio">Fraction of the claim's significant tokens found among its cited sources' content.</param>
    /// <param name="MatchedTokenCount">How many of the claim's significant tokens matched.</param>
    /// <param name="ClaimTokenCount">How many significant tokens the claim had in total.</param>
    public readonly record struct OverlapResult(bool IsSupported, double Ratio, int MatchedTokenCount, int ClaimTokenCount);

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        foreach (Match run in TokenPattern().Matches(text))
        {
            // A run of letters and digits can mix scripts ("IoT플랫폼"); each stretch is tokenized
            // the way its own script separates words.
            var value = run.Value;
            var start = 0;
            while (start < value.Length)
            {
                var ideographic = IsCjk(value[start]);
                var end = start + 1;
                while (end < value.Length && IsCjk(value[end]) == ideographic)
                {
                    end++;
                }

                var stretch = value[start..end];
                if (ideographic)
                {
                    for (var i = 0; i + 1 < stretch.Length; i++)
                    {
                        tokens.Add(stretch.Substring(i, 2));
                    }
                }
                else
                {
                    var word = stretch.ToLowerInvariant();
                    if (word.Length >= 3 && !StopWords.Contains(word))
                    {
                        tokens.Add(word);
                    }
                }

                start = end;
            }
        }

        return tokens;
    }

    /// <summary>Hangul, Han ideographs, Hiragana and Katakana — the scripts tokenized as character pairs.</summary>
    private static bool IsCjk(char c) => c switch
    {
        >= '\u1100' and <= '\u11FF' => true, // Hangul Jamo
        >= '\u3040' and <= '\u30FF' => true, // Hiragana, Katakana
        >= '\u3130' and <= '\u318F' => true, // Hangul Compatibility Jamo
        >= '\u3400' and <= '\u4DBF' => true, // CJK Unified Ideographs Extension A
        >= '\u4E00' and <= '\u9FFF' => true, // CJK Unified Ideographs
        >= '\uAC00' and <= '\uD7A3' => true, // Hangul Syllables
        >= '\uF900' and <= '\uFAFF' => true, // CJK Compatibility Ideographs
        _ => false,
    };

    [GeneratedRegex(@"[\p{L}\p{Mn}\p{Nd}]+")]
    private static partial Regex TokenPattern();
}
