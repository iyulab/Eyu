namespace Eyu.Core.Tests.Quality;

/// <summary>
/// Spearman's rank correlation with a permutation p-value. Ties take the average rank, which is
/// the usual convention and the one that matters here: an ablation ladder has only a handful of
/// distinct x values, so most x ranks are tied by design. The p-value is a two-sided permutation
/// test (y shuffled against a fixed x, seeded) rather than the t approximation, because the
/// sample sizes this is written for — a few levels times a few repetitions — are too small for
/// the approximation to be trusted and a permutation test is exact in expectation at any size.
/// </summary>
internal static class SpearmanCorrelation
{
    public const int DefaultPermutations = 10_000;

    public static SpearmanResult Compute(IReadOnlyList<(double X, double Y)> points, int seed, int permutations = DefaultPermutations)
    {
        if (points.Count < 3)
        {
            throw new ArgumentException("A rank correlation over fewer than three points ranks nothing.", nameof(points));
        }

        if (permutations < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(permutations), permutations, "At least one permutation is needed for a p-value.");
        }

        var xRanks = Ranks([.. points.Select(p => p.X)]);
        var yRanks = Ranks([.. points.Select(p => p.Y)]);
        var observed = Pearson(xRanks, yRanks);
        if (double.IsNaN(observed))
        {
            return new SpearmanResult(double.NaN, double.NaN, points.Count);
        }

        var random = new Random(seed);
        var shuffled = (double[])yRanks.Clone();
        var atLeastAsExtreme = 0;
        for (var permutation = 0; permutation < permutations; permutation++)
        {
            Shuffle(shuffled, random);
            var rho = Pearson(xRanks, shuffled);
            if (Math.Abs(rho) >= Math.Abs(observed) - 1e-12)
            {
                atLeastAsExtreme++;
            }
        }

        // The observed arrangement counts as one of the permutations, so the estimate is never 0.
        var pValue = (atLeastAsExtreme + 1.0) / (permutations + 1.0);
        return new SpearmanResult(observed, pValue, points.Count);
    }

    /// <summary>Average ranks, 1-based; equal values share the mean of the ranks they span.</summary>
    internal static double[] Ranks(double[] values)
    {
        var order = Enumerable.Range(0, values.Length).OrderBy(i => values[i]).ToArray();
        var ranks = new double[values.Length];
        var start = 0;
        while (start < order.Length)
        {
            var end = start;
            while (end + 1 < order.Length && values[order[end + 1]] == values[order[start]])
            {
                end++;
            }

            var averageRank = (start + end) / 2.0 + 1;
            for (var i = start; i <= end; i++)
            {
                ranks[order[i]] = averageRank;
            }

            start = end + 1;
        }

        return ranks;
    }

    private static double Pearson(double[] a, double[] b)
    {
        var meanA = a.Average();
        var meanB = b.Average();
        var covariance = 0.0;
        var varianceA = 0.0;
        var varianceB = 0.0;
        for (var i = 0; i < a.Length; i++)
        {
            var da = a[i] - meanA;
            var db = b[i] - meanB;
            covariance += da * db;
            varianceA += da * da;
            varianceB += db * db;
        }

        return varianceA == 0 || varianceB == 0 ? double.NaN : covariance / Math.Sqrt(varianceA * varianceB);
    }

    private static void Shuffle(double[] values, Random random)
    {
        for (var i = values.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (values[i], values[j]) = (values[j], values[i]);
        }
    }
}

/// <summary>
/// <see cref="Rho"/> is <see cref="double.NaN"/> when either variable is constant — there is no
/// ranking to correlate, which is different from a correlation of zero and is reported as such.
/// </summary>
internal sealed record SpearmanResult(double Rho, double PValue, int N);
