using Eyu.Core.Grounding;
using Xunit;

namespace Eyu.Core.Tests.Grounding;

public class GroundedClaimTests
{
    [Fact]
    public void Create_rejects_a_claim_with_no_sources()
    {
        // README: "A claim that can't cite its sources cannot be expressed — this is enforced
        // by the output shape, not by a prompt."
        Assert.Throws<ArgumentException>(() =>
            GroundedClaim.Create("the invoice total is $100", sources: []));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_blank_claim(string? claim)
    {
        Assert.Throws<ArgumentException>(() =>
            GroundedClaim.Create(claim!, sources: [new SourceRef("rec-1")]));
    }

    [Fact]
    public void Create_succeeds_and_exposes_its_shape_through_the_grounding_contract()
    {
        var source = new SourceRef("rec-1", "total");

        IGroundingContract claim = GroundedClaim.Create(
            "the invoice total is $100",
            sources: [source],
            path: ["extract", "sum"]);

        Assert.Equal("the invoice total is $100", claim.Claim);
        Assert.Equal([source], claim.Sources);
        Assert.Equal(["extract", "sum"], claim.Path);
    }

    [Fact]
    public void Create_defaults_path_to_empty_when_not_given()
    {
        var claim = GroundedClaim.Create("x", sources: [new SourceRef("rec-1")]);

        Assert.Empty(claim.Path);
    }
}
