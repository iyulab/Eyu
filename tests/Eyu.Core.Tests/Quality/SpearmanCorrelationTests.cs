using Xunit;

namespace Eyu.Core.Tests.Quality;

public class SpearmanCorrelationTests
{
    private const int Seed = 1;

    private static IReadOnlyList<(double, double)> Points(params (double, double)[] points) => points;

    [Fact]
    public void A_perfectly_monotone_relation_has_rho_one_and_a_small_p_value()
    {
        var result = SpearmanCorrelation.Compute(Points((0, 0.1), (1, 0.4), (2, 0.5), (3, 0.9), (4, 0.95), (5, 1.0), (6, 1.2), (7, 1.5)), Seed);

        Assert.Equal(1.0, result.Rho, precision: 12);
        Assert.True(result.PValue < 0.01, $"p was {result.PValue}");
        Assert.Equal(8, result.N);
    }

    [Fact]
    public void A_reversed_relation_has_rho_minus_one()
    {
        var result = SpearmanCorrelation.Compute(Points((0, 5), (1, 4), (2, 3), (3, 2), (4, 1)), Seed);

        Assert.Equal(-1.0, result.Rho, precision: 12);
    }

    // Rank correlation ignores spacing: a step function that only goes up is as monotone as a line.
    [Fact]
    public void Only_order_matters_not_spacing()
    {
        var result = SpearmanCorrelation.Compute(Points((0, 0), (1, 0.01), (2, 0.02), (3, 100)), Seed);

        Assert.Equal(1.0, result.Rho, precision: 12);
    }

    // The ablation's shape: four distinct x values, each repeated. Tied x share the average rank,
    // and a y that rises with x still correlates strongly — without tie handling the ranks would
    // depend on input order and so would rho.
    [Fact]
    public void Tied_x_values_share_the_average_rank()
    {
        Assert.Equal([1.5, 1.5, 3.5, 3.5, 5.0], SpearmanCorrelation.Ranks([0, 0, 1, 1, 2]));

        var inOrder = SpearmanCorrelation.Compute(Points((0, 0.2), (0, 0.3), (1, 0.5), (1, 0.6), (2, 0.9), (2, 1.0)), Seed);
        var shuffled = SpearmanCorrelation.Compute(Points((2, 1.0), (0, 0.2), (1, 0.6), (0, 0.3), (2, 0.9), (1, 0.5)), Seed);

        Assert.Equal(inOrder.Rho, shuffled.Rho, precision: 12);
        Assert.True(inOrder.Rho > 0.9, $"rho was {inOrder.Rho}");
    }

    [Fact]
    public void A_constant_variable_has_no_correlation_to_report()
    {
        var result = SpearmanCorrelation.Compute(Points((0, 1), (1, 1), (2, 1), (3, 1)), Seed);

        Assert.True(double.IsNaN(result.Rho));
        Assert.True(double.IsNaN(result.PValue));
    }

    [Fact]
    public void No_relation_has_rho_near_zero_and_a_large_p_value()
    {
        var result = SpearmanCorrelation.Compute(Points((0, 0.5), (0, 0.9), (1, 0.9), (1, 0.5), (2, 0.5), (2, 0.9), (3, 0.9), (3, 0.5)), Seed);

        Assert.Equal(0.0, result.Rho, precision: 12);
        Assert.True(result.PValue > 0.5, $"p was {result.PValue}");
    }

    [Fact]
    public void The_same_seed_gives_the_same_p_value()
    {
        var points = Points((0, 0.2), (1, 0.1), (2, 0.6), (3, 0.5), (4, 0.9), (5, 0.7));

        Assert.Equal(SpearmanCorrelation.Compute(points, Seed).PValue, SpearmanCorrelation.Compute(points, Seed).PValue);
    }

    [Fact]
    public void Too_few_points_are_refused()
    {
        Assert.Throws<ArgumentException>(() => SpearmanCorrelation.Compute(Points((0, 0), (1, 1)), Seed));
    }
}
