namespace Eyu.Core.Proposals;

/// <summary>
/// Lexical comparison of type and relation names: case and separators are ignored, so
/// <c>WorkOrder</c>, <c>work_order</c> and <c>work-order</c> are one name. Nothing else is folded —
/// no plurals, no synonyms — because mapping one word onto another is the term normalization this
/// library deliberately does not perform (design rationale, §B).
/// </summary>
internal static class VocabularyName
{
    public static string Normalize(string name)
        => new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
