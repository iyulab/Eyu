using Xunit;

namespace Eyu.Core.Tests.Quality;

public class ValueLikeNameTests
{
    [Theory]
    [InlineData("2024-03-11")]
    [InlineData("2024.3.11")]
    [InlineData("2024/03")]
    [InlineData("2024년 3월 11일")]
    [InlineData("2012년")]
    public void A_date_is_recognized(string name) => Assert.Equal(ValueLikeName.Kind.Date, ValueLikeName.Classify(name));

    [Theory]
    [InlineData("200")]
    [InlineData("1,250.5")]
    [InlineData("92 PSI")]
    [InlineData("200톤")]
    [InlineData("3.5 kg")]
    public void A_number_with_at_most_a_short_unit_is_recognized(string name) => Assert.Equal(ValueLikeName.Kind.Number, ValueLikeName.Classify(name));

    // Identifiers and names that merely contain digits are things, not values.
    [Theory]
    [InlineData("Vogtle 3")]
    [InlineData("WO-2024-0311")]
    [InlineData("737823 BOEING aircraft")]
    [InlineData("프레스 3호기")]
    [InlineData("Press 3")]
    [InlineData("0252022001")]
    public void A_name_that_only_contains_digits_is_not_a_value(string name) => Assert.Equal(ValueLikeName.Kind.None, ValueLikeName.Classify(name));

    [Fact]
    public void A_long_name_is_flagged_for_review_by_word_count()
    {
        Assert.True(ValueLikeName.IsLong("first-article inspection needed after the die change"));
        Assert.False(ValueLikeName.IsLong("Automatic Reactor Trip"));
    }
}
