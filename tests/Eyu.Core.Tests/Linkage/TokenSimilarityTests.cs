using Eyu.Core.Linkage;
using Xunit;

namespace Eyu.Core.Tests.Linkage;

public class TokenSimilarityTests
{
    [Fact]
    public void Tokens_are_runs_of_letters_and_digits()
    {
        Assert.Equal(
            ["clickart", "950", "000", "premier", "image", "pack", "dvd", "rom", "서울시", "강남구"],
            TokenSimilarity.Tokenize("clickart 950 000 - premier image pack (dvd-rom) 서울시,강남구"));
        Assert.Empty(TokenSimilarity.Tokenize(" -- () "));
    }

    [Theory]
    [InlineData("a b c", "c b a", 1.0)]
    [InlineData("a b c", "a b d", 0.5)]     // 2 shared of 4 distinct
    [InlineData("a a b", "a b", 1.0)]       // sets: repeats do not count twice
    [InlineData("a b", "c d", 0.0)]
    [InlineData("--", "()", 1.0)]           // no words on either side
    [InlineData("a", "--", 0.0)]
    public void Jaccard_is_shared_words_over_all_words(string a, string b, double expected)
    {
        Assert.Equal(expected, TokenSimilarity.Jaccard(a, b), 10);
        Assert.Equal(expected, TokenSimilarity.Jaccard(b, a), 10);
    }

    [Fact]
    public void Monge_elkan_forgives_order_and_a_misspelt_word()
    {
        var reordered = TokenSimilarity.MongeElkan("PHOTOSHOP ELEMENTS ADOBE", "ADOBE PHOTOSHOP ELEMENTS");
        var misspelt = TokenSimilarity.MongeElkan("ADOBE PHOTOSHOP ELEMENTS", "ADOBE PHOTOSHP ELEMENTS");

        Assert.Equal(1.0, reordered, 10);
        Assert.InRange(misspelt, 0.97, 1.0);
        Assert.Equal(0.0, TokenSimilarity.Jaccard("PHOTOSHOP", "PHOTOSHP")); // Jaccard has no partial credit
    }

    [Fact]
    public void Monge_elkan_is_symmetric_where_the_one_directional_score_is_not()
    {
        // One direction alone: every word of the short value is in the long one (1.0); the long value's
        // extra words find no counterpart. Linkage pairs are unordered, so the score must not depend on order.
        var shortFirst = TokenSimilarity.MongeElkan("ADOBE", "ADOBE PHOTOSHOP ELEMENTS 9");
        var longFirst = TokenSimilarity.MongeElkan("ADOBE PHOTOSHOP ELEMENTS 9", "ADOBE");

        Assert.Equal(shortFirst, longFirst, 10);
        Assert.InRange(shortFirst, 0.5, 0.9);
    }

    [Fact]
    public void Monge_elkan_reads_hangul_words_by_their_letters()
    {
        // One final consonant apart: by syllable one of three characters differs, by letter one of nine.
        Assert.True(TokenSimilarity.MongeElkan("서울 김민준", "서울 김민중") > 0.95);
        Assert.Equal(1.0, TokenSimilarity.MongeElkan("--", "()"), 10);
        Assert.Equal(0.0, TokenSimilarity.MongeElkan("a", "--"), 10);
    }
}
