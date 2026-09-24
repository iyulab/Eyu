using System.Text;
using Eyu.Core.Primitives;

namespace Eyu.Core.Proposals;

/// <summary>
/// Lexical comparison of type and relation names: case and separators are ignored, so
/// <c>WorkOrder</c>, <c>work_order</c> and <c>work-order</c> are one name, and so is a name written in
/// another Unicode form of the same characters (<see cref="TextForm.Fold"/>). Nothing else is folded —
/// no plurals, no synonyms — because mapping one word onto another is the term normalization this
/// library deliberately does not perform (design rationale, §B).
/// </summary>
/// <remarks>Compiled into Eyu.Rdf as a linked source file, like <see cref="TextForm"/>.</remarks>
internal static class VocabularyName
{
    public static string Normalize(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var rune in TextForm.Fold(name).EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune))
            {
                builder.Append(Rune.ToLowerInvariant(rune).ToString());
            }
        }

        return builder.ToString();
    }
}
