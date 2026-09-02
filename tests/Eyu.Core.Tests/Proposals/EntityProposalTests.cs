using Eyu.Core.Grounding;
using Eyu.Core.Proposals;
using Xunit;

namespace Eyu.Core.Tests.Proposals;

public class EntityProposalTests
{
    private static GroundedClaim SampleClaim() =>
        GroundedClaim.Create("records rec-1 and rec-2 denote the same person", sources: [new SourceRef("rec-1"), new SourceRef("rec-2")]);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_blank_entity_id(string? entityId)
    {
        Assert.Throws<ArgumentException>(() =>
            EntityProposal.Create(entityId!, "Person", SampleClaim(), VocabularyOrigin.Innate, confidence: 0.8));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_blank_entity_type(string? entityType)
    {
        Assert.Throws<ArgumentException>(() =>
            EntityProposal.Create("e1", entityType!, SampleClaim(), VocabularyOrigin.Innate, confidence: 0.8));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Create_rejects_confidence_outside_the_zero_to_one_range(double confidence)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EntityProposal.Create("e1", "Person", SampleClaim(), VocabularyOrigin.Innate, confidence));
    }

    [Fact]
    public void Create_succeeds_with_a_valid_entity_id_type_and_confidence()
    {
        var claim = SampleClaim();

        var proposal = EntityProposal.Create("e1", "Person", claim, VocabularyOrigin.Acquired, confidence: 0.42);

        Assert.Equal("e1", proposal.EntityId);
        Assert.Equal("Person", proposal.EntityType);
        Assert.Equal(claim, proposal.Claim);
        Assert.Equal(VocabularyOrigin.Acquired, proposal.Origin);
        Assert.Equal(0.42, proposal.Confidence);
    }
}
