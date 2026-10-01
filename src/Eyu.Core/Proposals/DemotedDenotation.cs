namespace Eyu.Core.Proposals;

/// <summary>
/// A record the model's answer put under an entity's <see cref="EntityProposal.DenotedBy"/> that was
/// moved to its <see cref="EntityProposal.MentionedIn"/>: the record holds the entity's name in
/// <see cref="Field"/>, which the caller declared as the field a relation runs through
/// (<see cref="Declared.DeclaredRelation.ViaField"/>) — the key of the relation's other end, so the
/// record refers to the entity rather than being a record of it. The entity and the citation stay;
/// only the identity claim is withdrawn.
/// </summary>
/// <param name="EntityId">The entity, by its <see cref="EntityProposal.EntityId"/>.</param>
/// <param name="RecordId">The record that no longer denotes it.</param>
/// <param name="Field">The declared reference field whose value is the entity's name.</param>
public sealed record DemotedDenotation(string EntityId, string RecordId, string Field);
