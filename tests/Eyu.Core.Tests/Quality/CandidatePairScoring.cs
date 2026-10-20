using System.Text;
using Eyu.Core.Linkage;

namespace Eyu.Core.Tests.Quality;

/// <summary>One scored candidate pair: how the pre-filter classified it, its ratio, and the label.</summary>
internal readonly record struct ScoredPair(LinkageClassification Classification, double LogLikelihoodRatio, bool IsMatch);

/// <summary>Pair-level precision, recall and F1, with the counts they come from.</summary>
internal readonly record struct PairCounts(int TruePositive, int FalsePositive, int FalseNegative)
{
    public double Precision => TruePositive + FalsePositive == 0 ? 0 : (double)TruePositive / (TruePositive + FalsePositive);

    public double Recall => TruePositive + FalseNegative == 0 ? 0 : (double)TruePositive / (TruePositive + FalseNegative);

    public double F1 => Precision + Recall == 0 ? 0 : 2 * Precision * Recall / (Precision + Recall);
}

/// <summary>
/// How a benchmark that hands out labeled candidate pairs (an entity-matching benchmark's test split) is
/// scored against the pre-filter, kept outside the measurement so it can be held to expectations
/// without the data. Three readings of one classification: the pre-filter alone (a gray-zone pair is
/// not a match — nothing confirmed it), the pre-filter with every gray-zone pair adjudicated correctly
/// (the ceiling a model step could reach), and the share of pairs left gray (the work handed to it).
/// The best single threshold over the ratios is reported beside them as an oracle — it is chosen on
/// the labels being scored, so it shows what the ratios separate, never what the pipeline achieves.
/// </summary>
internal static class CandidatePairScoring
{
    public static PairCounts PreFilterAlone(IReadOnlyList<ScoredPair> pairs) =>
        Count(pairs, p => p.Classification == LinkageClassification.Match);

    public static PairCounts GrayAdjudicatedCorrectly(IReadOnlyList<ScoredPair> pairs) =>
        Count(pairs, p => p.Classification == LinkageClassification.Match
            || (p.Classification == LinkageClassification.GrayZone && p.IsMatch));

    public static double GrayShare(IReadOnlyList<ScoredPair> pairs) =>
        pairs.Count == 0 ? 0 : (double)pairs.Count(p => p.Classification == LinkageClassification.GrayZone) / pairs.Count;

    /// <summary>The highest F1 any single threshold on the ratio reaches, and that threshold.</summary>
    public static (double F1, double Threshold) OracleThreshold(IReadOnlyList<ScoredPair> pairs)
    {
        var positives = pairs.Count(p => p.IsMatch);
        if (positives == 0)
        {
            return (0, double.PositiveInfinity);
        }

        // Walk thresholds from the highest ratio down: each step admits every pair at that ratio.
        var best = (F1: 0.0, Threshold: double.PositiveInfinity);
        int truePositive = 0, falsePositive = 0;
        foreach (var group in pairs.GroupBy(p => p.LogLikelihoodRatio).OrderByDescending(g => g.Key))
        {
            foreach (var pair in group)
            {
                if (pair.IsMatch)
                {
                    truePositive++;
                }
                else
                {
                    falsePositive++;
                }
            }

            var f1 = new PairCounts(truePositive, falsePositive, positives - truePositive).F1;
            if (f1 > best.F1)
            {
                best = (f1, group.Key);
            }
        }

        return best;
    }

    private static PairCounts Count(IReadOnlyList<ScoredPair> pairs, Func<ScoredPair, bool> predictedMatch)
    {
        int tp = 0, fp = 0, fn = 0;
        foreach (var pair in pairs)
        {
            var predicted = predictedMatch(pair);
            if (predicted && pair.IsMatch)
            {
                tp++;
            }
            else if (predicted)
            {
                fp++;
            }
            else if (pair.IsMatch)
            {
                fn++;
            }
        }

        return new PairCounts(tp, fp, fn);
    }
}

/// <summary>
/// A comma-separated file as RFC 4180 writes it: a quoted field may hold commas, doubled quotes and line
/// breaks. Enough for a benchmark's tables; not a general CSV library.
/// </summary>
internal static class Rfc4180
{
    public static List<Dictionary<string, string>> ReadRecords(string text)
    {
        var rows = ReadRows(text);
        if (rows.Count == 0)
        {
            return [];
        }

        var header = rows[0];
        return rows.Skip(1)
            .Where(row => !(row.Count == 1 && row[0].Length == 0))
            .Select(row =>
            {
                if (row.Count != header.Count)
                {
                    throw new FormatException($"A row has {row.Count} fields where the header has {header.Count}.");
                }

                return header.Zip(row).ToDictionary(p => p.First, p => p.Second, StringComparer.Ordinal);
            })
            .ToList();
    }

    public static List<List<string>> ReadRows(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var i = 0;
        if (text.Length > 0 && text[0] == '﻿')
        {
            i = 1;
        }

        for (; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"' when field.Length == 0:
                    quoted = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = [];
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (quoted)
        {
            throw new FormatException("The text ends inside a quoted field.");
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }
}
