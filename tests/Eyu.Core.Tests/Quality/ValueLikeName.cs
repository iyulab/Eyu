using System.Text.RegularExpressions;

namespace Eyu.Core.Tests.Quality;

/// <summary>
/// Whether a proposed entity's name reads as a field value rather than a thing: a date, or a number
/// with at most a short unit. A proposal has no attribute channel, so a record's value fields can
/// only come back as entities; this is what counts how often they do. It recognizes the unambiguous
/// shapes only — a long free-text name may be a note or may be an event's title, so it is listed
/// for review, never counted.
/// </summary>
internal static partial class ValueLikeName
{
    public enum Kind
    {
        None,
        Date,
        Number,
    }

    /// <summary>Names with at least this many words are listed for review as possible free-text values.</summary>
    public const int LongNameWords = 6;

    public static Kind Classify(string name)
    {
        var trimmed = name.Trim();
        if (DatePattern().IsMatch(trimmed))
        {
            return Kind.Date;
        }

        return NumberPattern().IsMatch(trimmed) && !LooksLikeIdentifier(trimmed) ? Kind.Number : Kind.None;
    }

    // A bare run of digits with a leading zero or longer than a measured quantity usually is: a report
    // number, a code. Such a name is a thing the records identify, not a value they state.
    private static bool LooksLikeIdentifier(string name) =>
        name.All(char.IsAsciiDigit) && (name.Length > 6 || (name.Length > 1 && name[0] == '0'));

    public static bool IsLong(string name) =>
        name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length >= LongNameWords;

    // 2024-03-11 · 2024.3.11 · 2024/03 · 2024년 3월 11일 · 2024년
    [GeneratedRegex(@"^\d{4}([-./]\d{1,2}([-./]\d{1,2})?|년(\s*\d{1,2}월(\s*\d{1,2}일)?)?)$")]
    private static partial Regex DatePattern();

    // 200 · 1,250.5 · 92 PSI · 200톤 · 3.5 kg — a number and at most a short unit word.
    [GeneratedRegex(@"^[-+]?\d[\d,]*(\.\d+)?\s*[\p{L}%°]{0,4}$")]
    private static partial Regex NumberPattern();
}
