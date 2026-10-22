using System.Text;
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
/// <see cref="DeclaredRelation.ViaField"/> of each of S's <see cref="DeclaredRelationKind.Reference"/> and
/// <see cref="DeclaredRelationKind.Parent"/> relations, and through the <see cref="DeclaredRelation.ViaField"/> of every other subject's
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

    /// <summary>
    /// The scope a record can only be the same thing within, read from the declarations: a subject some
    /// declaration names as the target of a <see cref="DeclaredRelationKind.Child"/> relation, or that
    /// declares a <see cref="DeclaredRelationKind.Parent"/> relation itself (a child read without its
    /// parent), holds rows owned by a parent — a sheet's checked items, an order's lines — and the
    /// relation's <see cref="DeclaredRelation.ViaField"/> names the parent. Two rows of different parents are two
    /// rows whatever their cells say (the same item checked on two sheets is two readings), so their
    /// scopes differ and <see cref="LinkagePipeline"/> does not compare them; rows of one parent are
    /// still compared. The parent key stays out of what is compared (<see cref="IdentityView"/>) — it
    /// bounds which pairs exist instead of weighing in them. A <see cref="DeclaredRelationKind.Reference"/>
    /// alone owns nothing (logs naming one machine are not the machine's parts), a record without a
    /// subject or without the parent key has no scope, and a subject owned through several fields is
    /// scoped by all of them. Keys compare as blocking keys do (case, surrounding whitespace and Unicode
    /// form aside).
    /// <para>
    /// The parent key is exactly what <see cref="IdentityView"/> removes, so the scope of
    /// <paramref name="records"/> — the call's records as sampled — is read from them up front and found
    /// again by id when it is asked of their identity view. Any other record (a known entity's) is read
    /// from its own fields.
    /// </para>
    /// </summary>
    public static Func<RawRecord, string?> OwnershipScope(IReadOnlyList<RawRecord> records, IReadOnlyList<DeclaredStructure> declaredStructures)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(declaredStructures);

        var ownerFields = new Dictionary<SubjectRef, SortedSet<string>>();
        void Own(SubjectRef owned, string? field)
        {
            if (string.IsNullOrWhiteSpace(field))
            {
                return;
            }

            if (!ownerFields.TryGetValue(owned, out var fields))
            {
                ownerFields[owned] = fields = new SortedSet<string>(StringComparer.Ordinal);
            }

            fields.Add(field);
        }

        foreach (var structure in declaredStructures)
        {
            foreach (var relation in structure.Relations)
            {
                switch (relation.Kind)
                {
                    case DeclaredRelationKind.Child:
                        Own(relation.Target, relation.ViaField);
                        break;
                    case DeclaredRelationKind.Parent:
                        Own(structure.Subject, relation.ViaField);
                        break;
                }
            }
        }

        if (ownerFields.Count == 0)
        {
            return _ => null;
        }

        string? Read(RawRecord record)
        {
            if (record.Subject is not { } subject || !ownerFields.TryGetValue(subject, out var fields))
            {
                return null;
            }

            var key = new StringBuilder(subject.ToString());
            foreach (var field in fields)
            {
                if (!record.Fields.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value))
                {
                    return null;
                }

                key.Append('\u001F').Append(field).Append('=').Append(TextForm.Fold(value).Trim().ToUpperInvariant());
            }

            return key.ToString();
        }

        var sampled = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            sampled.TryAdd(record.Id, Read(record));
        }

        return record => sampled.TryGetValue(record.Id, out var scope) ? scope : Read(record);
    }

    /// <summary>
    /// Which pairs are known to be different things, read from the declarations: two records of one
    /// subject whose declared <see cref="DeclaredStructure.KeyField"/> holds different values. A table's
    /// rows with different keys are different rows by construction — a pair of them is a
    /// <em>cannot-link</em>, not a pair to weigh — and weighing them anyway lets a fit on a small batch,
    /// which holds no true duplicate to learn from, read shared incidental values (a quantity, a note) as
    /// identity. <see cref="LinkagePipeline"/> neither compares nor fits on such a pair. Two records with
    /// the same key are still compared, records of different subjects are compared as before, and a
    /// record without a subject, of a subject that declares no key, or without a value in it is known
    /// distinct from nothing. Keys compare as blocking keys do (case, surrounding whitespace and Unicode
    /// form aside).
    /// <para>
    /// The key is read from <paramref name="records"/> as sampled and found again by id, as
    /// <see cref="OwnershipScope"/> reads its parent key — the pipeline is handed their identity view,
    /// which may no longer carry it. Any other record (a known entity's) is read from its own fields.
    /// </para>
    /// <para>
    /// The pairs are left out, not split apart: a record that matches two keyed records of one subject
    /// through a record of another can still draw them into one cluster.
    /// </para>
    /// </summary>
    public static Func<RawRecord, RawRecord, bool> DistinctByKey(IReadOnlyList<RawRecord> records, IReadOnlyList<DeclaredStructure> declaredStructures)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(declaredStructures);

        var keyFields = declaredStructures
            .Where(d => !string.IsNullOrWhiteSpace(d.KeyField))
            .ToDictionary(d => d.Subject, d => d.KeyField!.Trim());
        if (keyFields.Count == 0)
        {
            return (_, _) => false;
        }

        (SubjectRef Subject, string Key)? Read(RawRecord record) =>
            record.Subject is { } subject
            && keyFields.TryGetValue(subject, out var field)
            && record.Fields.TryGetValue(field, out var value)
            && !string.IsNullOrWhiteSpace(value)
                ? (subject, TextForm.Fold(value).Trim().ToUpperInvariant())
                : null;

        var sampled = new Dictionary<string, (SubjectRef Subject, string Key)?>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            sampled.TryAdd(record.Id, Read(record));
        }

        (SubjectRef Subject, string Key)? KeyOf(RawRecord record) => sampled.TryGetValue(record.Id, out var key) ? key : Read(record);

        return (a, b) => KeyOf(a) is { } left && KeyOf(b) is { } right
            && left.Subject == right.Subject
            && !string.Equals(left.Key, right.Key, StringComparison.Ordinal);
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
                    case DeclaredRelationKind.Parent:
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
