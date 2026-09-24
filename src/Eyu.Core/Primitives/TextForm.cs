using System.Text;

namespace Eyu.Core.Primitives;

/// <summary>
/// The Unicode forms Eyu reads and writes text in. Text that looks the same can arrive as different
/// code points: Hangul precomposed (NFC) or decomposed into jamo (NFD — how text saved on macOS
/// commonly arrives), Latin letters and digits in their full-width forms. Wherever Eyu decides that
/// two pieces of text are the same — a type or relation name, a field value, the words a claim shares
/// with its source — it compares them folded (<see cref="Fold"/>), so what renders identically
/// compares identically. What it writes out for people to read is <see cref="Canonical"/>.
/// </summary>
/// <remarks>
/// Compiled into Eyu.Rdf as a linked source file, so both packages apply one rule without a public
/// surface on the core that exists only for that package.
/// </remarks>
internal static class TextForm
{
    /// <summary>
    /// Normalization Form KC: canonical and compatibility equivalents composed alike, so decomposed
    /// Hangul meets precomposed and full-width Latin meets ASCII. For comparison only — it is lossy
    /// (<c>①</c> becomes <c>1</c>), so text shown back to a reader is never folded.
    /// </summary>
    public static string Fold(string text)
        => text.IsNormalized(NormalizationForm.FormKC) ? text : text.Normalize(NormalizationForm.FormKC);

    /// <summary>Normalization Form C, the form RDF 1.1 asks IRIs and literal lexical forms to be in.</summary>
    public static string Canonical(string text)
        => text.IsNormalized(NormalizationForm.FormC) ? text : text.Normalize(NormalizationForm.FormC);
}
