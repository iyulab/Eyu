using Eyu.Core.Grounding;
using Eyu.Core.Proposals;
using Xunit;

namespace Eyu.Core.Tests.Proposals;

/// <summary>A confidence is a number in [0, 1]: not NaN, which compares false to both bounds and used to pass a range check written as «below 0 or above 1».</summary>
public class ProposalConfidenceTests
{
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.PositiveInfinity)]
    public void An_entity_proposal_refuses_a_confidence_outside_zero_to_one_and_not_a_number(double confidence)
    {
        var claim = GroundedClaim.Create("Press is equipment", [new SourceRef("r1")]);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EntityProposal.Create("e1", "Press", "Equipment", claim, InnateVocabulary.OfEntityType("Equipment"), confidence));
    }
}
