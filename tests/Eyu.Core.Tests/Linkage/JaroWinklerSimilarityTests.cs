using Eyu.Core.Linkage;
using Xunit;

namespace Eyu.Core.Tests.Linkage;

public class JaroWinklerSimilarityTests
{
    [Fact]
    public void Identical_strings_score_1()
    {
        Assert.Equal(1.0, JaroWinklerSimilarity.Compute("Acme Corp", "Acme Corp"));
    }

    [Fact]
    public void Both_empty_strings_score_1()
    {
        Assert.Equal(1.0, JaroWinklerSimilarity.Compute("", ""));
    }

    [Fact]
    public void One_empty_string_scores_0()
    {
        Assert.Equal(0.0, JaroWinklerSimilarity.Compute("", "Acme"));
    }

    [Fact]
    public void Completely_different_strings_score_low()
    {
        Assert.True(JaroWinklerSimilarity.Compute("Springfield", "Portland") < 0.6);
    }

    [Fact]
    public void Classic_Winkler_example_MARTHA_MARHTA_scores_about_0_961()
    {
        // Winkler (1990)'s own worked example: a transposition close to the string start.
        var similarity = JaroWinklerSimilarity.Compute("MARTHA", "MARHTA");

        Assert.InRange(similarity, 0.960, 0.962);
    }

    [Fact]
    public void A_shared_prefix_scores_higher_than_the_same_edit_distance_without_one()
    {
        var withSharedPrefix = JaroWinklerSimilarity.Compute("Nuclear Power Ler", "Nuclear Power Lar");
        var withoutSharedPrefix = JaroWinklerSimilarity.Compute("Xuclear Power Ler", "Yuclear Power Lar");

        Assert.True(withSharedPrefix > withoutSharedPrefix);
    }

    [Fact]
    public void Punctuation_only_differences_score_above_the_default_agreement_threshold()
    {
        var similarity = JaroWinklerSimilarity.Compute(
            "NUCLEAR POWER LER CO., LTD.",
            "NUCLEAR POWER LER CO LTD");

        Assert.True(similarity >= FieldComparator.DefaultStringSimilarityAgreementThreshold);
    }
}
