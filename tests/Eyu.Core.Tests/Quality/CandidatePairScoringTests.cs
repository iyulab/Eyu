using Eyu.Core.Linkage;
using Xunit;

namespace Eyu.Core.Tests.Quality;

public class CandidatePairScoringTests
{
    private static ScoredPair Pair(LinkageClassification classification, double ratio, bool match) => new(classification, ratio, match);

    private static readonly ScoredPair[] Pairs =
    [
        Pair(LinkageClassification.Match, 9, true),
        Pair(LinkageClassification.Match, 6, false),
        Pair(LinkageClassification.GrayZone, 1, true),
        Pair(LinkageClassification.GrayZone, 0, false),
        Pair(LinkageClassification.NonMatch, -7, true),
        Pair(LinkageClassification.NonMatch, -9, false),
    ];

    [Fact]
    public void The_pre_filter_alone_counts_a_gray_pair_as_no_match()
    {
        var counts = CandidatePairScoring.PreFilterAlone(Pairs);

        Assert.Equal(new PairCounts(1, 1, 2), counts);
        Assert.Equal(0.5, counts.Precision);
        Assert.Equal(1.0 / 3, counts.Recall, 12);
    }

    [Fact]
    public void Gray_pairs_adjudicated_correctly_add_their_matches_and_none_of_their_non_matches()
    {
        Assert.Equal(new PairCounts(2, 1, 1), CandidatePairScoring.GrayAdjudicatedCorrectly(Pairs));
        Assert.Equal(2.0 / 6, CandidatePairScoring.GrayShare(Pairs), 12);
    }

    [Fact]
    public void The_oracle_threshold_is_the_best_cut_over_the_ratios()
    {
        var (f1, threshold) = CandidatePairScoring.OracleThreshold(Pairs);

        // Cut at 9: P 1, R 1/3 → 0.5. At 1: P 2/3, R 2/3 → 0.667. At -7: P 3/5, R 1 → 0.75.
        Assert.Equal(0.75, f1, 12);
        Assert.Equal(-7, threshold);
    }

    [Fact]
    public void A_quoted_field_keeps_its_commas_quotes_and_line_breaks()
    {
        var records = Rfc4180.ReadRecords("id,title,price\r\n0,\"a, \"\"b\"\"\nc\",\r\n1,plain,9.99\n");

        Assert.Equal(2, records.Count);
        Assert.Equal("a, \"b\"\nc", records[0]["title"]);
        Assert.Equal("", records[0]["price"]);
        Assert.Equal("9.99", records[1]["price"]);
    }

    [Fact]
    public void A_row_that_does_not_fit_the_header_is_refused()
    {
        Assert.Throws<FormatException>(() => Rfc4180.ReadRecords("a,b\n1,2,3\n"));
        Assert.Throws<FormatException>(() => Rfc4180.ReadRecords("a,b\n\"open,2\n"));
    }
}
