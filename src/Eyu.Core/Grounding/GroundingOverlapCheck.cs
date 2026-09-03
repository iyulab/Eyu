using System.Text.RegularExpressions;
using Eyu.Core.Records;

namespace Eyu.Core.Grounding;

/// <summary>
/// Closes a gap <see cref="IGroundingContract"/> does not itself close: a cited
/// <see cref="SourceRef.RecordId"/> proves only that the id exists among the records a proposer
/// was given — it says nothing about whether the claim text is actually substantiated by that
/// record's content, so a source cited for the wrong reason passes the contract's own shape
/// check silently (BD-20260903-02). This is a textual-overlap heuristic, not a semantic
/// verifier — it flags a claim whose cited sources share too little vocabulary with the claim,
/// which catches a mismatched citation without a human or a second model call in the loop.
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
    /// significant tokens (length &gt;= 3, not a stop word) also appear in the content of the
    /// records its <see cref="IGroundingContract.Sources"/> cite. A source whose
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

    private static List<string> Tokenize(string text) =>
        TokenPattern().Matches(text)
            .Select(m => m.Value.ToLowerInvariant())
            .Where(t => t.Length >= 3 && !StopWords.Contains(t))
            .ToList();

    [GeneratedRegex("[A-Za-z0-9]+")]
    private static partial Regex TokenPattern();
}
