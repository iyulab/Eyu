using Eyu.Core.Primitives;
using Eyu.Core.Records;
using Eyu.Core.Tests.Quality;
using Xunit;

namespace Eyu.Core.Tests.Live.Llm;

/// <summary>Checks of how a round chooses its door, not of the model — see <see cref="FormbaseDoorTests"/> for why they sit with the live measurements.</summary>
public class SourceDoorTests
{
    private static readonly DoorCase Case = DoorCase.Parse(DoorCaseTests.Sample);

    [Theory]
    [InlineData(null, SourceDoorKind.Identity)]
    [InlineData("", SourceDoorKind.Identity)]
    [InlineData("identity", SourceDoorKind.Identity)]
    [InlineData("date-slash", SourceDoorKind.DateSlash)]
    [InlineData("date-compact", SourceDoorKind.DateCompact)]
    [InlineData("table", SourceDoorKind.Table)]
    [InlineData("formbase", SourceDoorKind.Formbase)]
    [InlineData("formbase-null", SourceDoorKind.FormbaseNull)]
    public void Parse_reads_each_door_by_its_name(string? value, SourceDoorKind expected)
    {
        Assert.Equal(expected, SourceDoor.Parse(value));
    }

    [Fact]
    public void Parse_refuses_a_door_it_does_not_know_and_lists_the_ones_it_does()
    {
        var failure = Assert.Throws<ArgumentException>(() => SourceDoor.Parse("csv"));

        Assert.Contains("formbase-null", failure.Message, StringComparison.Ordinal);
        Assert.Contains("'csv'", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SourceDoorKind.Identity, false)]
    [InlineData(SourceDoorKind.DateSlash, false)]
    [InlineData(SourceDoorKind.DateCompact, false)]
    [InlineData(SourceDoorKind.Table, true)]
    [InlineData(SourceDoorKind.Formbase, true)]
    [InlineData(SourceDoorKind.FormbaseNull, true)]
    public void Only_the_real_doors_read_the_door_cases(SourceDoorKind kind, bool expected)
    {
        Assert.Equal(expected, SourceDoor.ReadsDoorCases(kind));
    }

    [Fact]
    public void A_case_is_named_by_the_door_it_went_through()
    {
        Assert.Equal("inspections@table", SourceDoor.CaseName("inspections", SourceDoorKind.Table));
        Assert.Equal("inspections@formbase", SourceDoor.CaseName("inspections", SourceDoorKind.Formbase));
        Assert.Equal("inspections@formbase-null", SourceDoor.CaseName("inspections", SourceDoorKind.FormbaseNull));
    }

    [Fact]
    public async Task Each_real_door_builds_the_rows_its_own_way()
    {
        var table = await SourceDoor.BuildAsync(Case, SourceDoorKind.Table, TestContext.Current.CancellationToken);
        var formbase = await SourceDoor.BuildAsync(Case, SourceDoorKind.Formbase, TestContext.Current.CancellationToken);
        var formbaseNull = await SourceDoor.BuildAsync(Case, SourceDoorKind.FormbaseNull, TestContext.Current.CancellationToken);

        Assert.Equal("", table[0].Fields["violations"]);
        Assert.False(formbase[0].Fields.ContainsKey("violations"));
        Assert.True(formbaseNull[0].Fields.ContainsKey("violations"));
        Assert.Null(formbaseNull[0].Fields["violations"]);
    }

    [Theory]
    [InlineData(SourceDoorKind.Identity)]
    [InlineData(SourceDoorKind.DateSlash)]
    [InlineData(SourceDoorKind.DateCompact)]
    public async Task A_catalog_door_is_not_a_door_the_door_cases_are_read_through(SourceDoorKind kind)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => SourceDoor.BuildAsync(Case, kind, TestContext.Current.CancellationToken));
    }

    private static RawRecord Record(string? opened) =>
        new("r1", new Dictionary<string, string?> { ["opened"] = opened, ["name"] = "2024-07-02 shop", ["unset"] = null }, SubjectRef.Create("inspection"), "k1");

    [Fact]
    public void The_catalog_is_handed_over_as_it_is_unless_a_date_rewrite_is_asked_for()
    {
        RawRecord[] records = [Record("2024-07-02")];

        Assert.Same(records, SourceDoor.RewriteCatalog(records, SourceDoorKind.Identity));
    }

    [Fact]
    public void A_date_rewrite_changes_a_whole_date_value_and_nothing_else_about_the_record()
    {
        RawRecord[] records = [Record("2024-07-02")];

        var slash = Assert.Single(SourceDoor.RewriteCatalog(records, SourceDoorKind.DateSlash));
        var compact = Assert.Single(SourceDoor.RewriteCatalog(records, SourceDoorKind.DateCompact));

        Assert.Equal("02/07/2024", slash.Fields["opened"]);
        Assert.Equal("20240702", compact.Fields["opened"]);
        Assert.Equal("2024-07-02 shop", slash.Fields["name"]);
        Assert.Null(slash.Fields["unset"]);
        Assert.Equal(("r1", "k1"), (slash.Id, slash.Key));
        Assert.Equal(records[0].Subject, slash.Subject);
    }
}
