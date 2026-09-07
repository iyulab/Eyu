using Eyu.Core.Linkage;
using Xunit;

namespace Eyu.Core.Tests.Linkage;

public class LinkageOptionsTests
{
    [Fact]
    public void The_default_instance_matches_LinkageClassifier_and_FellegiSunterEstimator_own_defaults()
    {
        var options = LinkageOptions.Default;

        Assert.Equal(LinkageClassifier.DefaultMatchThreshold, options.MatchThreshold);
        Assert.Equal(LinkageClassifier.DefaultNonMatchThreshold, options.NonMatchThreshold);
        Assert.Equal(FellegiSunterEstimator.DefaultMaxIterations, options.MaxIterations);
        Assert.Equal(FellegiSunterEstimator.DefaultConvergenceTolerance, options.ConvergenceTolerance);
        Assert.False(options.UseStringSimilarityComparator);
        Assert.Equal(FieldComparator.DefaultStringSimilarityAgreementThreshold, options.StringSimilarityAgreementThreshold);
    }

    [Fact]
    public void Constructing_without_arguments_produces_a_record_equal_to_Default()
    {
        Assert.Equal(LinkageOptions.Default, new LinkageOptions());
    }

    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(-4.0, 4.0)]
    public void Thresholds_that_do_not_order_are_refused(double match, double nonMatch)
    {
        var options = new LinkageOptions(MatchThreshold: match, NonMatchThreshold: nonMatch);

        var error = Assert.Throws<ArgumentException>(options.Validate);

        Assert.Equal(nameof(LinkageOptions.MatchThreshold), error.ParamName);
    }

    [Fact]
    public void An_iteration_count_that_would_never_run_EM_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LinkageOptions(MaxIterations: 0).Validate());
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1e-4)]
    public void A_non_positive_tolerance_is_refused(double tolerance)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LinkageOptions(ConvergenceTolerance: tolerance).Validate());
    }

    [Fact]
    public void The_default_options_validate()
    {
        LinkageOptions.Default.Validate();
    }
}
