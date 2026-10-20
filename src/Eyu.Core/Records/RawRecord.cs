using Eyu.Core.Primitives;

namespace Eyu.Core.Records;

/// <summary>
/// A single raw record sampled from a caller's source, keyed by <paramref name="Id"/> so a later
/// grounded claim (<see cref="Eyu.Core.Grounding.SourceRef"/>) can cite exactly which record it
/// came from. <paramref name="Fields"/> is an opaque string bag — Eyu does not assume a type
/// system here; that judgment belongs to <c>IOntologyProposer</c>, not to the sampling seam.
/// What Eyu does assume, by default, is what a record <em>is</em>: one mention of one real-world
/// entity (a row, a submission, an entry), which is the premise the entity-resolution pre-filter
/// rests on. A record that is instead a fragment of a document — a text chunk and its metadata —
/// still proposes and grounds correctly, but must say so through
/// <see cref="Eyu.Core.Linkage.LinkageOptions.RecordsDenoteEntities"/>, or its batch is linked
/// on a premise it does not meet.
/// <para>
/// <paramref name="Subject"/> is the declared subject the record is of — the form type a document
/// was filed under, the table a row came from — when the caller knows it (a sample taken per subject
/// does). It is what lets a field's declared role be read for this record rather than guessed from
/// its name: the same <c>asset_tag</c> is an equipment register's own key and an inspection log's
/// reference to the equipment, and only the subject says which one a record holds
/// (<see cref="Eyu.Core.Linkage.LinkageEvidence"/>). Without it the record is read as before.
/// </para>
/// </summary>
public sealed record RawRecord(string Id, IReadOnlyDictionary<string, string?> Fields, SubjectRef? Subject = null);
