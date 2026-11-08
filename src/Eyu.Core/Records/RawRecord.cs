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
/// <para>
/// <paramref name="Key"/> is the record's identity <em>apart from any one version of it</em>, when the source has
/// one: a source that keeps corrections as new versions of a record (Formbase files each correction as another
/// document under the same record key) hands each version a new <paramref name="Id"/> — the id a grounded claim
/// cites, because it names exactly which version was read — while the record stays the same record, under one
/// <paramref name="Key"/>. It is what a reference held by <em>key</em> (a field whose value is the key of the
/// record it points at) can be resolved against, and what tells two versions of a record apart from two records.
/// It is <c>null</c> for a source with no such identity (a record of its own, never corrected). The proposer reads it:
/// a record with a key is shown to the model as <c>[record key: …]</c> beside its id, with a sentence saying a field
/// holding another record's key refers to that record, and a declared reference field whose value is the key of a
/// record the answer says denotes an entity is read as a reference to that entity, not as the referring record's own.
/// </para>
/// </summary>
public sealed record RawRecord(string Id, IReadOnlyDictionary<string, string?> Fields, SubjectRef? Subject = null, string? Key = null);
