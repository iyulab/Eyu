using Eyu.Core.Proposals;
using Eyu.Core.Routing;
using Xunit;

namespace Eyu.Core.Tests.Routing;

public class RoutingPolicyTests
{
    [Theory]
    [InlineData(-0.01, 0.0)]
    [InlineData(1.01, 0.5)]
    [InlineData(0.9, -0.01)]
    [InlineData(0.9, 1.01)]
    public void Thresholds_outside_the_unit_interval_are_refused(double autoApplyAt, double reviewAt)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RoutingThresholds(autoApplyAt, reviewAt).Validate());
    }

    [Fact]
    public void A_review_bound_above_the_auto_apply_bound_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new RoutingThresholds(AutoApplyAt: 0.5, ReviewAt: 0.6).Validate());
    }

    [Fact]
    public void Equal_bounds_are_allowed_and_collapse_the_review_tier()
    {
        var thresholds = new RoutingThresholds(AutoApplyAt: 0.7, ReviewAt: 0.7);
        thresholds.Validate();
        Assert.Equal(ProposalRoute.AutoApply, thresholds.Route(0.7));
        Assert.Equal(ProposalRoute.DraftOnly, thresholds.Route(0.69));
    }

    [Fact]
    public void Create_validates_both_regimes_up_front()
    {
        var good = new RoutingThresholds(0.9, 0.5);
        var bad = new RoutingThresholds(0.5, 0.9);
        Assert.Throws<ArgumentException>(() => RoutingPolicy.Create(good, bad));
        Assert.Throws<ArgumentException>(() => RoutingPolicy.Create(bad, good));
    }

    [Fact]
    public void Uniform_applies_the_same_thresholds_to_both_origins()
    {
        var thresholds = new RoutingThresholds(0.9, 0.5);
        var policy = RoutingPolicy.Uniform(thresholds);
        Assert.Same(thresholds, policy.For(VocabularyOrigin.Innate));
        Assert.Same(thresholds, policy.For(VocabularyOrigin.Acquired));
    }

    [Fact]
    public void For_returns_the_regime_that_matches_the_origin()
    {
        var innate = new RoutingThresholds(0.8, 0.4);
        var acquired = new RoutingThresholds(0.95, 0.6);
        var policy = RoutingPolicy.Create(innate, acquired);
        Assert.Same(innate, policy.For(VocabularyOrigin.Innate));
        Assert.Same(acquired, policy.For(VocabularyOrigin.Acquired));
    }
}
