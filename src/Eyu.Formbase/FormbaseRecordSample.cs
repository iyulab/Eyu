using System.Text.Json;
using Eyu.Core.Ports;
using Eyu.Core.Primitives;
using Eyu.Core.Records;
using Formbase.Core.Ports;
using Formbase.Core.Primitives;
using Formbase.Core.Projection;
using Formbase.Core.Schema;

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
/// <para>
/// A form type that is a repeated section of another (<see cref="SectionSource"/>) has no documents
/// of its own: its records are the rows Formbase projects for it, cut from the parent's standing
/// documents the same way (<see cref="SectionRows"/>) — each row a record of the section, with the
/// parent's key value in the relation's key field, under the parent document's id, a slash and the
/// row's position. A parent whose section cannot be read contributes no rows, as it contributes none
/// to the projection.
/// </para>
/// </summary>
public sealed class FormbaseRecordSample(IRawStore rawStore, IFieldHintSource hintSource) : IRecordSample
{
    public async Task<IReadOnlyList<RawRecord>> SampleAsync(SubjectRef subject, int maxCount, CancellationToken cancellationToken = default)
    {
        var type = FormTypeRef.Create(subject.Value);
        var section = await SectionOfAsync(type, cancellationToken).ConfigureAwait(false);
        var documents = new List<StoredDocument>();

        await foreach (var document in rawStore.StreamAsync(section?.ParentType ?? type, Watermark.Zero, cancellationToken).ConfigureAwait(false))
        {
            documents.Add(document);
        }

        // A standing record is never a retirement, so its body is present.
        var standing = RecordFold.Latest(documents);
        if (section is null)
        {
            return standing
                .Take(maxCount)
                .Select(document => new RawRecord(document.Id.ToString(), Flatten(document.Body!.Root), subject))
                .ToList();
        }

        return standing
            .SelectMany(document => SectionRows.Expand(document, section).Rows
                .Select(row => new RawRecord($"{document.Id}/{row.Position}", Flatten(row.Body), subject)))
            .Take(maxCount)
            .ToList();
    }

    /// <summary>The section <paramref name="type"/> is, or null when it is a form type with documents of its own.</summary>
    private async Task<SectionDef?> SectionOfAsync(FormTypeRef type, CancellationToken cancellationToken)
    {
        if (await hintSource.GetSectionSourceAsync(type, cancellationToken).ConfigureAwait(false) is not { } source)
        {
            return null;
        }

        // The section names its key field; a section declaration Formbase accepted always declares it.
        var declaration = await hintSource.GetHintsAsync(type, cancellationToken).ConfigureAwait(false);
        return declaration is null ? null : SectionDef.From(source, declaration);
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
