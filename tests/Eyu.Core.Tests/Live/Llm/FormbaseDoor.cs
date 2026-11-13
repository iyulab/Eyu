using System.Text.Json;
using Eyu.Core.Primitives;
using Eyu.Core.Records;
using Eyu.Core.Tests.Quality;
using Eyu.Formbase;
using Formbase.Core.InMemory;
using Formbase.Core.Primitives;

namespace Eyu.Core.Tests.Live.Llm;

/// <summary>How the intake that fed Formbase wrote a blank cell of the source table.</summary>
internal enum BlankCell
{
    /// <summary>The field is left out of the document — what a JSON export of a table with missing values does.</summary>
    Omitted,

    /// <summary>The field is present as JSON <c>null</c> — what a form that was submitted with the field empty does.</summary>
    Null,
}

/// <summary>
/// The Formbase door of a <see cref="DoorCase"/>: the rows are filed as JSON documents in an in-memory raw
/// store and sampled back through <see cref="FormbaseRecordSample"/> — the adapter's own code, not a copy of
/// what it does — so a change to how the adapter writes a record reaches the measurement. What differs from the
/// table door (<see cref="DoorCase.ThroughTable"/>) is exactly what a different door can differ in without the
/// information changing: how a blank cell is written, the record's identity, and the record key.
/// <para>
/// A document is filed under an id minted from the case and the row's position, so the prompt is the same on
/// every build. The natural key column, when the case has one and the row has it, is also filed as the
/// document's record key — as an intake that knows its key does — and stays among the fields as well.
/// </para>
/// </summary>
internal static class FormbaseDoor
{
    /// <summary>
    /// The case's rows as the Formbase door hands them to Eyu, in the order they were filed. <paramref name="withKey"/> is false
    /// for the door that files no record key at all — the one variable the measurement of the key's effect changes.
    /// </summary>
    public static async Task<RawRecord[]> ThroughAsync(DoorCase doorCase, BlankCell blank, CancellationToken cancellationToken, bool withKey = true)
    {
        var store = new InMemoryRawStore();
        var type = FormTypeRef.Create(doorCase.Subject);
        for (var index = 0; index < doorCase.Rows.Count; index++)
        {
            var row = doorCase.Rows[index];
            var body = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var column in doorCase.Columns)
            {
                if (!DoorCase.IsBlank(row[column]))
                {
                    body[column] = row[column];
                }
                else if (blank == BlankCell.Null)
                {
                    body[column] = null;
                }
            }

            RecordKey? key = withKey && doorCase.KeyColumn is { } keyColumn && !DoorCase.IsBlank(row[keyColumn])
                ? RecordKey.Create(row[keyColumn])
                : null;
            await store.AppendAsync(
                type,
                DocumentId.From(doorCase.RowGuid(index)),
                DocumentBody.Parse(JsonSerializer.Serialize(body)),
                key,
                cancellationToken);
        }

        var sample = new FormbaseRecordSample(store, new InMemoryFieldHintSource());
        return [.. await sample.SampleAsync(SubjectRef.Create(doorCase.Subject), doorCase.Rows.Count, cancellationToken)];
    }
}
