using Xunit;

namespace Eyu.Core.Tests.Quality;

public class CrossSourceJoinTests
{
    private static CrossSourceCase Plant => QualityCatalog.CrossSourceCases.Single(c => c.Name == "plant-erp-and-cmms");

    // A source's declared reference field is only read where its records hold that field; a field
    // named in a declaration but absent from every record would declare nothing and pass unnoticed.
    [Fact]
    public void Every_declared_reference_field_is_a_field_of_its_sources_records()
    {
        foreach (var source in QualityCatalog.CrossSourceCases.SelectMany(c => c.Sources))
        {
            foreach (var field in source.Declarations.SelectMany(d => d.Relations).Select(r => r.ViaField).OfType<string>())
            {
                Assert.Contains(source.Records, r => r.Fields.ContainsKey(field));
            }
        }

        Assert.All(Plant.Sources, s => Assert.NotEmpty(s.Declarations));
    }

    [Theory]
    [InlineData("프레스 4호기", "press 4")]
    [InlineData("Press 04 (A line)", "press 4")]
    [InlineData("press 04 a-line", "press 4")]   // case and separators ignored
    [InlineData("압축기2", "compressor 2")]
    public void A_name_of_a_thing_joins_to_that_thing(string name, string thing)
    {
        Assert.Equal(thing, Assert.Single(CrossSourceJoin.Match(Plant, name)).Label);
    }

    [Theory]
    [InlineData("PRS-004 / CNV-012 (프레스 4호기 / 컨베이어 12)")]
    [InlineData("프레스 4호기 / 컨베이어 12")]
    public void A_name_carrying_two_things_is_over_merged(string name)
    {
        Assert.Empty(CrossSourceJoin.Match(Plant, name));
        Assert.Equal(["press 4", "conveyor 12"], CrossSourceJoin.OverMerged(Plant, name).Select(t => t.Label));
    }

    [Theory]
    [InlineData("CMP-002 (공기압축기 2호)")]  // one thing named two ways at once is not two things
    [InlineData("공기압축기 2호")]            // «압축기2» inside it is the same thing's other name
    [InlineData("WO-5531")]
    public void A_name_carrying_at_most_one_thing_is_not_over_merged(string name)
    {
        Assert.Empty(CrossSourceJoin.OverMerged(Plant, name));
    }
}
