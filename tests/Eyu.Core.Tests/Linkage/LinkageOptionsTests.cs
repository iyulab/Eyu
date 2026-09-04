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
    }

    [Fact]
    public void Constructing_without_arguments_produces_a_record_equal_to_Default()
    {
        Assert.Equal(LinkageOptions.Default, new LinkageOptions());
    }
}
