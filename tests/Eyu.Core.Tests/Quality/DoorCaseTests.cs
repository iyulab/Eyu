using Eyu.Core.Primitives;
using Xunit;

namespace Eyu.Core.Tests.Quality;

public class DoorCaseTests
{
    internal const string Sample = """
        {
          "name": "inspections",
          "subject": "food inspection",
          "key_column": "inspection_id",
          "columns": ["inspection_id", "dba_name", "violations"],
          "seed": 1,
          "rows": [
            {"inspection_id": "10", "dba_name": "CITGO", "violations": ""},
            {"inspection_id": "11", "dba_name": "Ace Cafe", "violations": "38. PESTS - Comments: GAP"},
            {"inspection_id": "12", "dba_name": "", "violations": ""}
          ]
        }
        """;

    [Fact]
    public void Parse_reads_the_name_subject_columns_key_and_rows_in_order()
    {
        var doorCase = DoorCase.Parse(Sample);

        Assert.Equal("inspections", doorCase.Name);
        Assert.Equal("food inspection", doorCase.Subject);
        Assert.Equal("inspection_id", doorCase.KeyColumn);
        Assert.Equal(["inspection_id", "dba_name", "violations"], doorCase.Columns);
        Assert.Equal(["10", "11", "12"], doorCase.Rows.Select(r => r["inspection_id"]));
    }

    [Fact]
    public void The_key_column_is_optional()
    {
        var doorCase = DoorCase.Parse("""{"name":"n","subject":"s","columns":["a"],"rows":[{"a":"1"}]}""");

        Assert.Null(doorCase.KeyColumn);
    }

    [Fact]
    public void The_table_door_keeps_every_cell_as_text_and_a_blank_as_the_empty_string()
    {
        var records = DoorCase.Parse(Sample).ThroughTable();

        Assert.Equal(3, records.Length);
        Assert.Equal("CITGO", records[0].Fields["dba_name"]);
        Assert.Equal("", records[0].Fields["violations"]);
        Assert.Equal("", records[2].Fields["dba_name"]);
        Assert.All(records, r => Assert.Equal(3, r.Fields.Count));
    }

    [Fact]
    public void The_table_door_has_no_record_key_and_calls_a_row_by_its_position()
    {
        var records = DoorCase.Parse(Sample).ThroughTable();

        Assert.Equal(["row-1", "row-2", "row-3"], records.Select(r => r.Id));
        Assert.All(records, r => Assert.Null(r.Key));
    }

    [Fact]
    public void Both_doors_hand_the_subject_over_unchanged()
    {
        var records = DoorCase.Parse(Sample).ThroughTable();

        Assert.All(records, r => Assert.Equal(SubjectRef.Create("food inspection"), r.Subject));
    }

    [Fact]
    public void A_row_identity_is_stable_across_calls_and_differs_per_row_and_per_case()
    {
        var doorCase = DoorCase.Parse(Sample);
        var other = doorCase with { Name = "another" };

        Assert.Equal(doorCase.RowGuid(1), doorCase.RowGuid(1));
        Assert.NotEqual(doorCase.RowGuid(0), doorCase.RowGuid(1));
        Assert.NotEqual(doorCase.RowGuid(0), other.RowGuid(0));
    }

    [Theory]
    [InlineData("""[1]""")]
    [InlineData("""{"subject":"s","columns":["a"],"rows":[{"a":"1"}]}""")]
    [InlineData("""{"name":"n","subject":" ","columns":["a"],"rows":[{"a":"1"}]}""")]
    [InlineData("""{"name":"n","subject":"s","columns":[],"rows":[{"a":"1"}]}""")]
    [InlineData("""{"name":"n","subject":"s","columns":["a","a"],"rows":[{"a":"1"}]}""")]
    [InlineData("""{"name":"n","subject":"s","columns":["a"],"rows":[]}""")]
    [InlineData("""{"name":"n","subject":"s","columns":["a"],"key_column":"b","rows":[{"a":"1"}]}""")]
    [InlineData("""{"name":"n","subject":"s","columns":["a","b"],"rows":[{"a":"1"}]}""")]
    [InlineData("""{"name":"n","subject":"s","columns":["a"],"rows":[{"a":1}]}""")]
    [InlineData("""{"name":"n","subject":"s","columns":["a"],"rows":[{"a":null}]}""")]
    public void Parse_refuses_a_file_that_does_not_hold_what_the_measurement_relies_on(string json)
    {
        Assert.Throws<FormatException>(() => DoorCase.Parse(json, "broken.json"));
    }

    [Fact]
    public void A_refusal_names_the_file()
    {
        var failure = Assert.Throws<FormatException>(() => DoorCase.Parse("""{"name":"n"}""", "broken.json"));

        Assert.StartsWith("broken.json:", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadFolder_reads_the_case_files_in_name_order_and_skips_the_manifest()
    {
        var folder = Directory.CreateTempSubdirectory("eyu-door-cases-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "b-case.json"), Sample.Replace("inspections", "second", StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(folder, "a-case.json"), Sample.Replace("inspections", "first", StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(folder, "manifest.json"), """{"a-case":{"rows":3}}""");

            Assert.Equal(["first", "second"], DoorCase.LoadFolder(folder).Select(c => c.Name));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_folder_name_that_does_not_exist_is_an_error_not_an_empty_measurement()
    {
        Assert.Throws<DirectoryNotFoundException>(() => DoorCase.LoadFolder(Path.Combine(Path.GetTempPath(), "eyu-door-cases-does-not-exist")));
    }
}
