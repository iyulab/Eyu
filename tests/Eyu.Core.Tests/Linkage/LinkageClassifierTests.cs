using Eyu.Core.Linkage;
using Xunit;

namespace Eyu.Core.Tests.Linkage;

public class LinkageClassifierTests
{
    [Theory]
    [InlineData(4.0, LinkageClassification.Match)]
    [InlineData(10.0, LinkageClassification.Match)]
    [InlineData(0.0, LinkageClassification.GrayZone)]
    [InlineData(3.99, LinkageClassification.GrayZone)]
    [InlineData(-3.99, LinkageClassification.GrayZone)]
    [InlineData(-4.0, LinkageClassification.NonMatch)]
    [InlineData(-10.0, LinkageClassification.NonMatch)]
    public void Classify_uses_the_default_thresholds(double llr, LinkageClassification expected)
    {
        Assert.Equal(expected, LinkageClassifier.Classify(llr));
    }

    [Fact]
    public void Classify_honors_explicit_thresholds()
    {
        Assert.Equal(LinkageClassification.Match, LinkageClassifier.Classify(1.5, matchThreshold: 1.0, nonMatchThreshold: -1.0));
    }

    [Fact]
    public void Classify_rejects_a_match_threshold_at_or_below_the_non_match_threshold()
    {
        Assert.Throws<ArgumentException>(() => LinkageClassifier.Classify(0.0, matchThreshold: -1.0, nonMatchThreshold: 1.0));
    }
}
