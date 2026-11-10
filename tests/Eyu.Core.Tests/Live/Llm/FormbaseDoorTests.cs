using Eyu.Core.Primitives;
using Eyu.Core.Records;
using Eyu.Core.Tests.Quality;
using Xunit;

namespace Eyu.Core.Tests.Live.Llm;

/// <summary>
/// Checks of the instrument that builds the two doors, not of the model: they need no endpoint, but they
/// compile only with the live measurements (<c>IncludeEyuLlmLiveTests</c>), which is where Formbase is
/// referenced — run them with that flag and <c>--filter-class "*FormbaseDoorTests*"</c>.
/// </summary>
public class FormbaseDoorTests
{
    private static readonly DoorCase Case = DoorCase.Parse(DoorCaseTests.Sample);

    private static Task<RawRecord[]> Through(BlankCell blank) =>
        FormbaseDoor.ThroughAsync(Case, blank, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Every_row_comes_back_once_in_the_order_it_was_filed()
    {
        var records = await Through(BlankCell.Omitted);

        Assert.Equal(["10", "11", "12"], records.Select(r => r.Fields["inspection_id"]));
    }

    [Fact]
    public async Task A_blank_cell_is_a_missing_field_when_the_intake_leaves_it_out()
    {
        var records = await Through(BlankCell.Omitted);

        Assert.False(records[0].Fields.ContainsKey("violations"));
        Assert.False(records[2].Fields.ContainsKey("dba_name"));
        Assert.Equal("CITGO", records[0].Fields["dba_name"]);
    }

    [Fact]
    public async Task A_blank_cell_is_a_null_field_when_the_intake_files_it_as_null()
    {
        var records = await Through(BlankCell.Null);

        Assert.True(records[0].Fields.ContainsKey("violations"));
        Assert.Null(records[0].Fields["violations"]);
        Assert.Null(records[2].Fields["dba_name"]);
    }

    [Fact]
    public async Task The_natural_key_is_the_record_key_and_stays_a_field_too()
    {
        var records = await Through(BlankCell.Omitted);

        Assert.Equal(["10", "11", "12"], records.Select(r => r.Key));
        Assert.Equal(["10", "11", "12"], records.Select(r => r.Fields["inspection_id"]));
    }

    [Fact]
    public async Task A_case_without_a_key_column_files_documents_without_a_record_key()
    {
        var records = await FormbaseDoor.ThroughAsync(Case with { KeyColumn = null }, BlankCell.Omitted, TestContext.Current.CancellationToken);

        Assert.All(records, r => Assert.Null(r.Key));
    }

    [Fact]
    public async Task A_record_is_identified_by_the_stable_document_id_of_its_row()
    {
        var first = await Through(BlankCell.Omitted);
        var again = await Through(BlankCell.Null);

        Assert.Equal(first.Select(r => r.Id), again.Select(r => r.Id));
        Assert.Equal([Case.RowGuid(0).ToString(), Case.RowGuid(1).ToString(), Case.RowGuid(2).ToString()], first.Select(r => r.Id));
    }

    [Fact]
    public async Task The_subject_is_the_same_value_the_table_door_carries()
    {
        var records = await Through(BlankCell.Omitted);

        Assert.All(records, r => Assert.Equal(SubjectRef.Create("food inspection"), r.Subject));
        Assert.Equal(Case.ThroughTable().Select(r => r.Subject), records.Select(r => r.Subject));
    }

    /// <summary>
    /// The premise of the measurement: the two doors hold the same information and differ only in how a blank
    /// is written, in the record's identity and in the record key. A cell that differed any other way would be
    /// an effect of the instrument, read as an effect of the door.
    /// </summary>
    [Fact]
    public async Task The_two_doors_differ_only_in_blank_cells_identity_and_key()
    {
        var table = Case.ThroughTable();
        var formbase = await Through(BlankCell.Omitted);

        for (var row = 0; row < table.Length; row++)
        {
            var fromTable = table[row].Fields.Where(f => !DoorCase.IsBlank(f.Value!)).OrderBy(f => f.Key, StringComparer.Ordinal).ToList();
            var fromFormbase = formbase[row].Fields.OrderBy(f => f.Key, StringComparer.Ordinal).ToList();
            Assert.Equal(fromTable, fromFormbase);
        }

        Assert.NotEqual(table.Select(r => r.Id), formbase.Select(r => r.Id));
        Assert.NotEqual(table.Select(r => r.Key), formbase.Select(r => r.Key));
    }
}
