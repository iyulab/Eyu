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
    private const char HangulSyllableBase = '가';
    private const char LeadingConsonantBase = 'ᄀ';
    private const char VowelBase = 'ᅡ';
    private const char TrailingConsonantBase = 'ᆧ';
    private const int LeadingConsonantCount = 19;
    private const int VowelCount = 21;
    private const int TrailingConsonantCount = 28;
    private const int HangulSyllableCount = LeadingConsonantCount * VowelCount * TrailingConsonantCount;

    /// <summary>
    /// Normalization Form KC: canonical and compatibility equivalents composed alike, so decomposed
    /// Hangul meets precomposed and full-width Latin meets ASCII. For comparison only — it is lossy
    /// (<c>①</c> becomes <c>1</c>), so text shown back to a reader is never folded.
    /// </summary>
    /// <remarks>
    /// The two equivalences named above hold in every host, including one built with
    /// <c>InvariantGlobalization</c> (as Native AOT applications commonly are), where the runtime's
    /// normalization leaves non-ASCII text as it is: Hangul jamo are composed and full-width ASCII is
    /// narrowed here by the arithmetic Unicode defines for them, after whatever the runtime did. The
    /// rest of NFKC (<c>①</c>, ligatures) needs the runtime's tables and is not promised in such a host.
    /// </remarks>
    public static string Fold(string text)
        => ComposeHangul(text.IsNormalized(NormalizationForm.FormKC) ? text : text.Normalize(NormalizationForm.FormKC), narrowFullWidth: true);

    /// <summary>Normalization Form C, the form RDF 1.1 asks IRIs and literal lexical forms to be in.</summary>
    /// <remarks>Hangul is composed in every host, as for <see cref="Fold"/>.</remarks>
    public static string Canonical(string text)
        => ComposeHangul(text.IsNormalized(NormalizationForm.FormC) ? text : text.Normalize(NormalizationForm.FormC), narrowFullWidth: false);

    /// <summary>
    /// Hangul syllables spelled out as their jamo — leading consonant, vowel and, when there is one,
    /// trailing consonant (Unicode §3.12) — and everything else as it is. <c>김민준</c> and <c>김민중</c>
    /// then differ in one letter of nine rather than in one character of three, which is how far apart
    /// a reader takes them to be.
    /// </summary>
    public static string DecomposeHangul(string text)
    {
        if (!text.Any(IsHangulSyllable))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length * 3);
        foreach (var c in text)
        {
            if (!IsHangulSyllable(c))
            {
                builder.Append(c);
                continue;
            }

            var index = c - HangulSyllableBase;
            builder.Append((char)(LeadingConsonantBase + (index / (VowelCount * TrailingConsonantCount))));
            builder.Append((char)(VowelBase + (index % (VowelCount * TrailingConsonantCount) / TrailingConsonantCount)));
            if (index % TrailingConsonantCount is var trailing and > 0)
            {
                builder.Append((char)(TrailingConsonantBase + trailing));
            }
        }

        return builder.ToString();
    }

    private static bool IsHangulSyllable(char c) => c >= HangulSyllableBase && c < HangulSyllableBase + HangulSyllableCount;

    private static bool IsLeadingConsonant(char c) => c >= LeadingConsonantBase && c < LeadingConsonantBase + LeadingConsonantCount;

    private static bool IsVowel(char c) => c >= VowelBase && c < VowelBase + VowelCount;

    private static bool IsTrailingConsonant(char c) => c > TrailingConsonantBase && c < TrailingConsonantBase + TrailingConsonantCount;

    private static bool IsFullWidthAscii(char c) => c is >= '！' and <= '～';

    private static string ComposeHangul(string text, bool narrowFullWidth)
    {
        if (!text.Any(c => IsLeadingConsonant(c) || IsVowel(c) || IsTrailingConsonant(c) || (narrowFullWidth && (IsFullWidthAscii(c) || c == '　'))))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            var last = builder.Length > 0 ? builder[^1] : '\0';
            if (IsLeadingConsonant(last) && IsVowel(c))
            {
                builder[^1] = (char)(HangulSyllableBase + ((((last - LeadingConsonantBase) * VowelCount) + (c - VowelBase)) * TrailingConsonantCount));
            }
            else if (IsHangulSyllable(last) && (last - HangulSyllableBase) % TrailingConsonantCount == 0 && IsTrailingConsonant(c))
            {
                builder[^1] = (char)(last + (c - TrailingConsonantBase));
            }
            else if (narrowFullWidth && IsFullWidthAscii(c))
            {
                builder.Append((char)(c - 0xFEE0));
            }
            else if (narrowFullWidth && c == '　')
            {
                builder.Append(' ');
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
