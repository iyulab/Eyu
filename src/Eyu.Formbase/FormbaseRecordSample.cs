using System.Text.Json;
using Eyu.Core.Ports;
using Eyu.Core.Primitives;
using Eyu.Core.Records;
using Formbase.Core.Ports;
using Formbase.Core.Primitives;
using Formbase.Core.Projection;

namespace Eyu.Formbase;

/// <summary>
/// Adapts a Formbase <see cref="IRawStore"/> to Eyu's <see cref="IRecordSample"/>. Each sampled
/// document's top-level JSON object properties become the record's opaque string fields; a
/// nested object or array is not recursively flattened, it survives as its own raw JSON text —
/// deeper structure is Eyu's judgment to interpret, not this adapter's to pre-decide. A
/// non-object document body (e.g. a bare JSON array) yields a record with no fields.
/// The sample is of records, not of appends: documents are folded the way Formbase's projection
/// folds them (<see cref="RecordFold.Latest"/>) — a corrected record appears once, as its latest
/// document, and a retired one not at all — so every document of the form type is read before the
/// first <c>maxCount</c> records are taken. A record appears under the id of the document that
/// stands for it, which changes when the record is corrected.
/// </summary>
public sealed class FormbaseRecordSample(IRawStore rawStore) : IRecordSample
{
    public async Task<IReadOnlyList<RawRecord>> SampleAsync(SubjectRef subject, int maxCount, CancellationToken cancellationToken = default)
    {
        var type = FormTypeRef.Create(subject.Value);
        var documents = new List<StoredDocument>();

        await foreach (var document in rawStore.StreamAsync(type, Watermark.Zero, cancellationToken).ConfigureAwait(false))
        {
            documents.Add(document);
        }

        // A standing record is never a retirement, so its body is present.
        return RecordFold.Latest(documents)
            .Take(maxCount)
            .Select(document => new RawRecord(document.Id.ToString(), Flatten(document.Body!.Root)))
            .ToList();
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
