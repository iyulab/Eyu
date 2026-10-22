using Eyu.Core.Primitives;
using Formbase.Core.InMemory;
using Formbase.Core.Primitives;
using Formbase.Core.Schema;
using Xunit;

namespace Eyu.Formbase.Tests;

public class FormbaseRecordSampleTests
{
    [Fact]
    public async Task SampleAsync_returns_empty_for_a_subject_with_no_documents()
    {
        var rawStore = new InMemoryRawStore();
        var sample = new FormbaseRecordSample(rawStore, new InMemoryFieldHintSource());

        var records = await sample.SampleAsync(SubjectRef.Create("invoice"), maxCount: 10, TestContext.Current.CancellationToken);

        Assert.Empty(records);
    }

    [Fact]
    public async Task SampleAsync_flattens_a_top_level_json_object_into_string_fields()
    {
        var rawStore = new InMemoryRawStore();
        var type = FormTypeRef.Create("invoice");
        await rawStore.AppendAsync(type, DocumentId.New(), DocumentBody.Parse("""{"lot":"L1","qty":3,"paid":true,"note":null}"""), cancellationToken: TestContext.Current.CancellationToken);
        var sample = new FormbaseRecordSample(rawStore, new InMemoryFieldHintSource());

        var records = await sample.SampleAsync(SubjectRef.Create("invoice"), maxCount: 10, TestContext.Current.CancellationToken);

        var record = Assert.Single(records);
        Assert.Equal("L1", record.Fields["lot"]);
        Assert.Equal("3", record.Fields["qty"]);
        Assert.Equal("true", record.Fields["paid"]);
        Assert.Null(record.Fields["note"]);
    }

    /// <summary>
    /// A sample is taken per form type, so each record knows its subject — what lets the linkage
    /// pre-filter read a field's declared role for this record rather than guess it from the name.
    /// </summary>
    [Fact]
    public async Task SampleAsync_records_the_subject_each_record_was_sampled_under()
    {
        var rawStore = new InMemoryRawStore();
        await rawStore.AppendAsync(FormTypeRef.Create("invoice"), DocumentId.New(), DocumentBody.Parse("""{"lot":"L1"}"""), cancellationToken: TestContext.Current.CancellationToken);

        var records = await new FormbaseRecordSample(rawStore, new InMemoryFieldHintSource()).SampleAsync(SubjectRef.Create("invoice"), maxCount: 10, TestContext.Current.CancellationToken);

        Assert.Equal(SubjectRef.Create("invoice"), Assert.Single(records).Subject);
    }

    [Fact]
    public async Task SampleAsync_returns_a_corrected_record_once_as_its_latest_document()
    {
        var rawStore = new InMemoryRawStore();
        var type = FormTypeRef.Create("invoice");
        var key = RecordKey.Create("INV-1");
        await rawStore.AppendAsync(type, DocumentId.New(), DocumentBody.Parse("""{"qty":3}"""), key, TestContext.Current.CancellationToken);
        var corrected = DocumentId.New();
        await rawStore.AppendAsync(type, corrected, DocumentBody.Parse("""{"qty":4}"""), key, TestContext.Current.CancellationToken);
        var sample = new FormbaseRecordSample(rawStore, new InMemoryFieldHintSource());

        var records = await sample.SampleAsync(SubjectRef.Create("invoice"), maxCount: 10, TestContext.Current.CancellationToken);

        var record = Assert.Single(records);
        Assert.Equal(corrected.ToString(), record.Id);
        Assert.Equal("4", record.Fields["qty"]);
    }

    [Fact]
    public async Task SampleAsync_leaves_out_a_retired_record()
    {
        var rawStore = new InMemoryRawStore();
        var type = FormTypeRef.Create("invoice");
        var key = RecordKey.Create("INV-1");
        await rawStore.AppendAsync(type, DocumentId.New(), DocumentBody.Parse("""{"qty":3}"""), key, TestContext.Current.CancellationToken);
        await rawStore.RetireAsync(type, DocumentId.New(), key, TestContext.Current.CancellationToken);
        await rawStore.AppendAsync(type, DocumentId.New(), DocumentBody.Parse("""{"qty":9}"""), cancellationToken: TestContext.Current.CancellationToken);
        var sample = new FormbaseRecordSample(rawStore, new InMemoryFieldHintSource());

        var records = await sample.SampleAsync(SubjectRef.Create("invoice"), maxCount: 10, TestContext.Current.CancellationToken);

        Assert.Equal("9", Assert.Single(records).Fields["qty"]);
    }

    [Fact]
    public async Task SampleAsync_passes_a_nested_value_through_as_its_raw_json_text()
    {
        // Documented limitation: this adapter flattens only the top level. A nested object or
        // array survives as opaque JSON text rather than being recursively flattened -- Eyu's own
        // judgment, not this adapter, is where deeper structure gets interpreted.
        var rawStore = new InMemoryRawStore();
        var type = FormTypeRef.Create("invoice");
        await rawStore.AppendAsync(type, DocumentId.New(), DocumentBody.Parse("""{"lines":[{"sku":"A"}]}"""), cancellationToken: TestContext.Current.CancellationToken);
        var sample = new FormbaseRecordSample(rawStore, new InMemoryFieldHintSource());

        var records = await sample.SampleAsync(SubjectRef.Create("invoice"), maxCount: 10, TestContext.Current.CancellationToken);

        Assert.Equal("""[{"sku":"A"}]""", Assert.Single(records).Fields["lines"]);
    }

    [Fact]
    public async Task SampleAsync_reports_a_non_object_document_as_having_no_fields()
    {
        var rawStore = new InMemoryRawStore();
        var type = FormTypeRef.Create("invoice");
        await rawStore.AppendAsync(type, DocumentId.New(), DocumentBody.Parse("[1,2,3]"), cancellationToken: TestContext.Current.CancellationToken);
        var sample = new FormbaseRecordSample(rawStore, new InMemoryFieldHintSource());

        var records = await sample.SampleAsync(SubjectRef.Create("invoice"), maxCount: 10, TestContext.Current.CancellationToken);

        Assert.Empty(Assert.Single(records).Fields);
    }

    [Fact]
    public async Task SampleAsync_stops_at_the_requested_max_count()
    {
        var rawStore = new InMemoryRawStore();
        var type = FormTypeRef.Create("invoice");
        for (var i = 0; i < 5; i++)
        {
            await rawStore.AppendAsync(type, DocumentId.New(), DocumentBody.Parse("{}"), cancellationToken: TestContext.Current.CancellationToken);
        }
        var sample = new FormbaseRecordSample(rawStore, new InMemoryFieldHintSource());

        var records = await sample.SampleAsync(SubjectRef.Create("invoice"), maxCount: 2, TestContext.Current.CancellationToken);

        Assert.Equal(2, records.Count);
    }

    [Fact]
    public async Task SampleAsync_uses_the_document_id_as_the_record_id()
    {
        var rawStore = new InMemoryRawStore();
        var type = FormTypeRef.Create("invoice");
        var id = DocumentId.New();
        await rawStore.AppendAsync(type, id, DocumentBody.Parse("{}"), cancellationToken: TestContext.Current.CancellationToken);
        var sample = new FormbaseRecordSample(rawStore, new InMemoryFieldHintSource());

        var records = await sample.SampleAsync(SubjectRef.Create("invoice"), maxCount: 10, TestContext.Current.CancellationToken);

        Assert.Equal(id.ToString(), Assert.Single(records).Id);
    }

    private static readonly FormTypeRef Log = FormTypeRef.Create("inspection-log");
    private static readonly FormTypeRef Item = FormTypeRef.Create("inspection-item");

    private static InMemoryFieldHintSource WithChecklist()
    {
        var hints = new InMemoryFieldHintSource();
        hints.Declare(new FormTypeHints(Item, "inspection_items",
        [
            new FieldHint("log_no", ColumnType.Text),
            new FieldHint("item", ColumnType.Text),
            new FieldHint("value", ColumnType.Decimal),
        ]));
        hints.Declare(new FormTypeHints(Log, "inspection_logs", [new FieldHint("log_no", ColumnType.Text)],
            [new RelationHint("items", RelationKind.Child, Item, "log_no", "items")]));
        return hints;
    }

    /// <summary>
    /// A checklist inside each log sheet is its own form type's records: one per row, carrying the sheet's
    /// number — the field that tells «the same item on another sheet» from «another row of this sheet».
    /// </summary>
    [Fact]
    public async Task SampleAsync_of_a_repeated_section_returns_its_rows_as_records()
    {
        var rawStore = new InMemoryRawStore();
        var sheet = DocumentId.New();
        await rawStore.AppendAsync(Log, sheet, DocumentBody.Parse("""{"log_no":"L-7","items":[{"item":"유압 압력","value":12.3},{"item":"유압 압력","value":12.4}]}"""), cancellationToken: TestContext.Current.CancellationToken);
        var sample = new FormbaseRecordSample(rawStore, WithChecklist());

        var records = await sample.SampleAsync(SubjectRef.Create(Item.Value), maxCount: 10, TestContext.Current.CancellationToken);

        Assert.Equal([$"{sheet}/0", $"{sheet}/1"], records.Select(r => r.Id));
        Assert.All(records, r => Assert.Equal("L-7", r.Fields["log_no"]));
        Assert.All(records, r => Assert.Equal(SubjectRef.Create(Item.Value), r.Subject));
        Assert.Equal(["12.3", "12.4"], records.Select(r => r.Fields["value"]));
    }

    [Fact]
    public async Task SampleAsync_of_a_repeated_section_follows_the_parents_corrections_and_skips_unreadable_sections()
    {
        var rawStore = new InMemoryRawStore();
        var key = RecordKey.Create("sheet-1");
        await rawStore.AppendAsync(Log, DocumentId.New(), DocumentBody.Parse("""{"log_no":"L-1","items":[{"item":"a"},{"item":"b"}]}"""), key, TestContext.Current.CancellationToken);
        var corrected = DocumentId.New();
        await rawStore.AppendAsync(Log, corrected, DocumentBody.Parse("""{"log_no":"L-1","items":[{"item":"a2"}]}"""), key, TestContext.Current.CancellationToken);
        await rawStore.AppendAsync(Log, DocumentId.New(), DocumentBody.Parse("""{"log_no":"L-2","items":"none"}"""), cancellationToken: TestContext.Current.CancellationToken);
        var sample = new FormbaseRecordSample(rawStore, WithChecklist());

        var records = await sample.SampleAsync(SubjectRef.Create(Item.Value), maxCount: 10, TestContext.Current.CancellationToken);

        var record = Assert.Single(records);
        Assert.Equal($"{corrected}/0", record.Id);
        Assert.Equal("a2", record.Fields["item"]);
    }

    /// <summary>The sheet's own record does not carry its checklist: those items are the section's records.</summary>
    [Fact]
    public async Task SampleAsync_of_a_parent_leaves_out_the_array_its_section_is_held_in()
    {
        var rawStore = new InMemoryRawStore();
        await rawStore.AppendAsync(Log, DocumentId.New(), DocumentBody.Parse("""{"log_no":"L-7","shift":"night","items":[{"item":"a"}]}"""), cancellationToken: TestContext.Current.CancellationToken);

        var record = Assert.Single(await new FormbaseRecordSample(rawStore, WithChecklist()).SampleAsync(SubjectRef.Create(Log.Value), maxCount: 10, TestContext.Current.CancellationToken));

        Assert.Equal(["log_no", "shift"], record.Fields.Keys);
    }

    [Fact]
    public async Task SampleAsync_of_a_repeated_section_takes_at_most_max_count_rows()
    {
        var rawStore = new InMemoryRawStore();
        await rawStore.AppendAsync(Log, DocumentId.New(), DocumentBody.Parse("""{"log_no":"L-1","items":[{"item":"a"},{"item":"b"},{"item":"c"}]}"""), cancellationToken: TestContext.Current.CancellationToken);

        var records = await new FormbaseRecordSample(rawStore, WithChecklist()).SampleAsync(SubjectRef.Create(Item.Value), maxCount: 2, TestContext.Current.CancellationToken);

        Assert.Equal(["a", "b"], records.Select(r => r.Fields["item"]));
    }
}
