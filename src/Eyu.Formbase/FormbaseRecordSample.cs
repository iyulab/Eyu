using System.Text.Json;
using Eyu.Core.Ports;
using Eyu.Core.Primitives;
using Eyu.Core.Records;
using Formbase.Core.Ports;
using Formbase.Core.Primitives;

namespace Eyu.Formbase;

/// <summary>
/// Adapts a Formbase <see cref="IRawStore"/> to Eyu's <see cref="IRecordSample"/>. Each sampled
/// document's top-level JSON object properties become the record's opaque string fields; a
/// nested object or array is not recursively flattened, it survives as its own raw JSON text —
/// deeper structure is Eyu's judgment to interpret, not this adapter's to pre-decide. A
/// non-object document body (e.g. a bare JSON array) yields a record with no fields.
/// </summary>
public sealed class FormbaseRecordSample(IRawStore rawStore) : IRecordSample
{
    public async Task<IReadOnlyList<RawRecord>> SampleAsync(SubjectRef subject, int maxCount, CancellationToken cancellationToken = default)
    {
        var type = FormTypeRef.Create(subject.Value);
        var records = new List<RawRecord>();

        await foreach (var document in rawStore.StreamAsync(type, Watermark.Zero, cancellationToken).ConfigureAwait(false))
        {
            if (records.Count >= maxCount)
            {
                break;
            }

            records.Add(new RawRecord(document.Id.ToString(), Flatten(document.Body.Root)));
        }

        return records;
    }

    private static Dictionary<string, string?> Flatten(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string?>();
        }

        var fields = new Dictionary<string, string?>();
        foreach (var property in root.EnumerateObject())
        {
            fields[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => property.Value.GetString(),
                _ => property.Value.GetRawText(),
            };
        }

        return fields;
    }
}
