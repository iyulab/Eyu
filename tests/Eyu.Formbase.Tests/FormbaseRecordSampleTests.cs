using Eyu.Core.Primitives;
using Formbase.Core.InMemory;
using Formbase.Core.Primitives;
using Xunit;

namespace Eyu.Formbase.Tests;

public class FormbaseRecordSampleTests
{
    [Fact]
    public async Task SampleAsync_returns_empty_for_a_subject_with_no_documents()
    {
        var rawStore = new InMemoryRawStore();
        var sample = new FormbaseRecordSample(rawStore);

        var records = await sample.SampleAsync(SubjectRef.Create("invoice"), maxCount: 10);

        Assert.Empty(records);
    }

    [Fact]
    public async Task SampleAsync_flattens_a_top_level_json_object_into_string_fields()
    {
        var rawStore = new InMemoryRawStore();
        var type = FormTypeRef.Create("invoice");
        await rawStore.AppendAsync(type, DocumentId.New(), DocumentBody.Parse("""{"lot":"L1","qty":3,"paid":true,"note":null}"""));
        var sample = new FormbaseRecordSample(rawStore);

        var records = await sample.SampleAsync(SubjectRef.Create("invoice"), maxCount: 10);

        var record = Assert.Single(records);
        Assert.Equal("L1", record.Fields["lot"]);
        Assert.Equal("3", record.Fields["qty"]);
        Assert.Equal("true", record.Fields["paid"]);
        Assert.Null(record.Fields["note"]);
    }

    [Fact]
    public async Task SampleAsync_passes_a_nested_value_through_as_its_raw_json_text()
    {
        // Documented limitation: this adapter flattens only the top level. A nested object or
        // array survives as opaque JSON text rather than being recursively flattened -- Eyu's own
        // judgment, not this adapter, is where deeper structure gets interpreted.
        var rawStore = new InMemoryRawStore();
        var type = FormTypeRef.Create("invoice");
        await rawStore.AppendAsync(type, DocumentId.New(), DocumentBody.Parse("""{"lines":[{"sku":"A"}]}"""));
        var sample = new FormbaseRecordSample(rawStore);

        var records = await sample.SampleAsync(SubjectRef.Create("invoice"), maxCount: 10);

        Assert.Equal("""[{"sku":"A"}]""", Assert.Single(records).Fields["lines"]);
    }

    [Fact]
    public async Task SampleAsync_reports_a_non_object_document_as_having_no_fields()
    {
        var rawStore = new InMemoryRawStore();
        var type = FormTypeRef.Create("invoice");
        await rawStore.AppendAsync(type, DocumentId.New(), DocumentBody.Parse("[1,2,3]"));
        var sample = new FormbaseRecordSample(rawStore);

        var records = await sample.SampleAsync(SubjectRef.Create("invoice"), maxCount: 10);

        Assert.Empty(Assert.Single(records).Fields);
    }

    [Fact]
    public async Task SampleAsync_stops_at_the_requested_max_count()
    {
        var rawStore = new InMemoryRawStore();
        var type = FormTypeRef.Create("invoice");
        for (var i = 0; i < 5; i++)
        {
            await rawStore.AppendAsync(type, DocumentId.New(), DocumentBody.Parse("{}"));
        }
        var sample = new FormbaseRecordSample(rawStore);

        var records = await sample.SampleAsync(SubjectRef.Create("invoice"), maxCount: 2);

        Assert.Equal(2, records.Count);
    }

    [Fact]
    public async Task SampleAsync_uses_the_document_id_as_the_record_id()
    {
        var rawStore = new InMemoryRawStore();
        var type = FormTypeRef.Create("invoice");
        var id = DocumentId.New();
        await rawStore.AppendAsync(type, id, DocumentBody.Parse("{}"));
        var sample = new FormbaseRecordSample(rawStore);

        var records = await sample.SampleAsync(SubjectRef.Create("invoice"), maxCount: 10);

        Assert.Equal(id.ToString(), Assert.Single(records).Id);
    }
}
