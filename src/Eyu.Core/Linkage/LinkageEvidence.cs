using Eyu.Core.Declared;
using Eyu.Core.Primitives;
using Eyu.Core.Records;

namespace Eyu.Core.Linkage;

/// <summary>
/// Which of a record's fields are evidence of the record's own identity. A field the caller declared
/// as a reference holds another entity's key — an inspection log's <c>asset_tag</c> names the
/// equipment it inspected — so two logs agreeing on it are two inspections of one machine, not one
/// record twice. Counted as identity evidence, such fields pre-linked distinct documents as a single
/// entity and told the model not to re-decide (measured: inspection logs and repair requests of the
/// same machine, grouped with the machine's register entry).
/// <para>
/// The role is read per subject, from the declaration: a record of subject S references through the
/// <see cref="DeclaredRelation.ViaField"/> of each of S's <see cref="DeclaredRelationKind.Reference"/>
/// relations, and through the <see cref="DeclaredRelation.ViaField"/> of every other subject's
/// <see cref="DeclaredRelationKind.Child"/> relation whose target is S (the child carries the key).
/// A relation that does not say its side (<see cref="DeclaredRelation.Kind"/> unset) is not read —
/// guessing would strip a record's own key. A record without a <see cref="RawRecord.Subject"/> keeps
/// every field: the same field name is often a register's own key and a log's reference to it, so
/// a name alone cannot say which.
/// </para>
/// <para>
/// Records of different subjects are still compared on what remains, so two form types that both
/// record the same thing's own facts (a sign-up form and a details-change form of one person) can
/// still be linked; a register entry and a log that only refers to it share no identity field left.
/// </para>
/// </summary>
public static class LinkageEvidence
{
    /// <summary>
    /// <paramref name="records"/> as the pre-filter should compare them: each record without the
    /// fields its subject declares as references, under the same id and subject. A record losing no
    /// field is returned as it was.
    /// </summary>
    public static IReadOnlyList<RawRecord> IdentityView(IReadOnlyList<RawRecord> records, IReadOnlyList<DeclaredStructure> declaredStructures)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(declaredStructures);

        var referenceFields = ReferenceFieldsBySubject(declaredStructures);
        if (referenceFields.Count == 0)
        {
            return records;
        }

        return records
            .Select(record =>
                record.Subject is { } subject
                && referenceFields.TryGetValue(subject, out var references)
                && record.Fields.Keys.Any(references.Contains)
                    ? record with
                    {
                        Fields = record.Fields
                            .Where(field => !references.Contains(field.Key))
                            .ToDictionary(field => field.Key, field => field.Value, StringComparer.Ordinal),
                    }
                    : record)
            .ToList();
    }

    /// <summary>The fields each subject's records hold another entity's key in, as declared.</summary>
    public static IReadOnlyDictionary<SubjectRef, IReadOnlySet<string>> ReferenceFieldsBySubject(IReadOnlyList<DeclaredStructure> declaredStructures)
    {
        ArgumentNullException.ThrowIfNull(declaredStructures);

        var bySubject = new Dictionary<SubjectRef, HashSet<string>>();
        void Add(SubjectRef subject, string? field)
        {
            if (string.IsNullOrWhiteSpace(field))
            {
                return;
            }

            if (!bySubject.TryGetValue(subject, out var fields))
            {
                bySubject[subject] = fields = new HashSet<string>(StringComparer.Ordinal);
            }

            fields.Add(field);
        }

        foreach (var structure in declaredStructures)
        {
            foreach (var relation in structure.Relations)
            {
                switch (relation.Kind)
                {
                    case DeclaredRelationKind.Reference:
                        Add(structure.Subject, relation.ViaField);
                        break;
                    case DeclaredRelationKind.Child:
                        Add(relation.Target, relation.ViaField);
                        break;
                }
            }
        }

        return bySubject.ToDictionary(p => p.Key, p => (IReadOnlySet<string>)p.Value);
    }
}
