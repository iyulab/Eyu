using Eyu.Core.Records;

namespace Eyu.Core.Judgment;

/// <summary>
/// Settles a type name the answer misspelled against a name in use — only on the evidence of the records
/// the answer cited. A model copying <c>saithe food product</c> out of a record sometimes writes
/// <c>saihe food product</c>: the name it meant is in use, and the record it cites says which. Nearness
/// alone never decides it (<c>Austria</c> and <c>Australia</c> are near and are two things); the cited
/// records do. The answered name is respelled as the name in use when all of these hold:
/// <list type="bullet">
/// <item>no name in use spells it (<see cref="TypeNames.SameType"/> settles that case first);</item>
/// <item>exactly one name in use is within <see cref="MaxEdits"/> letter edits of it, and the answered
/// name is at least <see cref="MinLetters"/> letters long — a short name is near too many others;</item>
/// <item>every word the name in use has and the answer lacks appears in a cited record, and no word the
/// answer has and the name in use lacks does — so the answered spelling is the one without a source.</item>
/// </list>
/// Anything else is left as answered: a near name the records do not settle stays the model's own.
/// </summary>
internal static class GroundedRespelling
{
    internal const int MaxEdits = 2;
    internal const int MinLetters = 6;

    /// <summary>The name in use <paramref name="answered"/> stands for, or null when the records do not settle one.</summary>
    internal static string? Find(string answered, IReadOnlyCollection<string> namesInUse, IReadOnlySet<string> citedWords)
    {
        var key = TypeNames.Key(answered);
        if (key.Length < MinLetters || namesInUse.Any(n => TypeNames.Key(n) == key))
        {
            return null;
        }

        var near = namesInUse.Where(n => Edits(key, TypeNames.Key(n), MaxEdits) <= MaxEdits).Distinct(StringComparer.Ordinal).Take(2).ToList();
        if (near.Count != 1)
        {
            return null;
        }

        var inUse = near[0];
        var answeredWords = Words(answered);
        var inUseWords = Words(inUse);
        var gained = inUseWords.Where(w => !answeredWords.Contains(w)).ToList();
        var lost = answeredWords.Where(w => !inUseWords.Contains(w)).ToList();
        return gained.Count > 0 && gained.All(citedWords.Contains) && !lost.Any(citedWords.Contains) ? inUse : null;
    }

    /// <summary>The words of the cited records' values, lowercased — what <see cref="Find"/> checks a spelling against.</summary>
    internal static HashSet<string> CitedWords(IEnumerable<string?> sourceIds, IReadOnlyDictionary<string, RawRecord> recordsById)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in sourceIds)
        {
            if (id is not null && recordsById.TryGetValue(id, out var record))
            {
                foreach (var value in record.Fields.Values)
                {
                    if (value is not null)
                    {
                        words.UnionWith(Words(value));
                    }
                }
            }
        }

        return words;
    }

    private static HashSet<string> Words(string text)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var inWord = i < text.Length && char.IsLetterOrDigit(text[i]);
            if (inWord && start < 0)
            {
                start = i;
            }
            else if (!inWord && start >= 0)
            {
                words.Add(text[start..i].ToLowerInvariant());
                start = -1;
            }
        }

        return words;
    }

    /// <summary>Levenshtein distance, cut off once it passes <paramref name="limit"/> (then <paramref name="limit"/> + 1).</summary>
    private static int Edits(string a, string b, int limit)
    {
        if (Math.Abs(a.Length - b.Length) > limit)
        {
            return limit + 1;
        }

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            var rowMin = current[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(substitution, Math.Min(previous[j], current[j - 1]) + 1);
                rowMin = Math.Min(rowMin, current[j]);
            }

            if (rowMin > limit)
            {
                return limit + 1;
            }

            (previous, current) = (current, previous);
        }

        return Math.Min(previous[b.Length], limit + 1);
    }
}
