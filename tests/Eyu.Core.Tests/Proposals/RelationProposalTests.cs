using Eyu.Core.Grounding;
using Eyu.Core.Proposals;
using Xunit;

namespace Eyu.Core.Tests.Proposals;

public class RelationProposalTests
{
    private static GroundedClaim SampleClaim() =>
        GroundedClaim.Create("e1 works_for e2", sources: [new SourceRef("rec-1")]);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_blank_relation_name(string? relationName)
    {
        Assert.Throws<ArgumentException>(() =>
            RelationProposal.Create(relationName!, "e1", "e2", SampleClaim(), VocabularyOrigin.Innate, confidence: 0.5));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Create_rejects_confidence_outside_the_zero_to_one_range(double confidence)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RelationProposal.Create("works_for", "e1", "e2", SampleClaim(), VocabularyOrigin.Innate, confidence));
    }

    [Fact]
    public void Create_succeeds_and_carries_both_endpoint_entity_ids()
    {
        var claim = SampleClaim();

        var proposal = RelationProposal.Create("works_for", "e1", "e2", claim, VocabularyOrigin.Innate, confidence: 0.9);

        Assert.Equal("works_for", proposal.RelationName);
        Assert.Equal("e1", proposal.FromEntityId);
        Assert.Equal("e2", proposal.ToEntityId);
        Assert.Equal(claim, proposal.Claim);
        Assert.Equal(0.9, proposal.Confidence);
    }
}
