using Eyu.Core.Declared;
using Eyu.Core.Proposals;

namespace Eyu.Core.Judgment;

/// <summary>
/// The deterministic half of "Declared always wins". The prompt tells the model that declared
/// structure is authoritative, but a sentence in a prompt is a request, not a guarantee — what a
/// caller can rely on is decided here, after the model has answered, without a second model call:
/// <list type="bullet">
/// <item>An entity whose type is the declared subject, and a relation whose name is a declared
/// relation, are stamped <see cref="ProposalBasis.Declared"/> — whatever the model said about
/// itself. Everything else is <see cref="ProposalBasis.Inferred"/>.</item>
/// <item>A relation proposed under a declared name whose ends contradict the declaration — it does
/// not leave the declared subject, or does not reach the declared target — is left out and reported
/// as <see cref="RejectionReason.ContradictsDeclaration"/>. The name is declared fact; a proposal that
/// uses the name for something else is inference dressed as declaration, and inference does not
/// win. It is reported rather than silently dropped so a caller measuring the model sees it.</item>
/// </list>
/// Type names are compared leniently (case and separators ignored: <c>WorkOrder</c>,
/// <c>work_order</c> and <c>work-order</c> are one name) because the model writes them and the
/// caller declares them in whatever convention each has. Nothing here touches
/// <see cref="VocabularyOrigin"/> or confidence: which vocabulary a type came from, and how sure
/// the model is about the <em>instance</em>, are different questions from whether the type was
/// declared.
/// </summary>
internal static class DeclaredStructureMerge
{
    public static OntologyProposal Apply(DeclaredStructure? declared, IReadOnlyList<EntityProposal> entities, IReadOnlyList<RelationProposal> relations, IReadOnlyList<ProposalRejection> rejections)
    {
        if (declared is null)
        {
            return new OntologyProposal(entities, relations, rejections);
        }

        var subject = Normalize(declared.Subject.Value);
        var mergedEntities = entities
            .Select(e => Normalize(e.EntityType) == subject ? e.WithBasis(ProposalBasis.Declared) : e)
            .ToList();
        var entityTypeById = mergedEntities.ToDictionary(e => e.EntityId, e => Normalize(e.EntityType), StringComparer.Ordinal);

        var mergedRejections = new List<ProposalRejection>(rejections);
        var declaredRelations = declared.Relations.ToDictionary(r => Normalize(r.Name), r => r);
        var mergedRelations = new List<RelationProposal>(relations.Count);
        foreach (var relation in relations)
        {
            if (!declaredRelations.TryGetValue(Normalize(relation.RelationName), out var declaredRelation))
            {
                mergedRelations.Add(relation);
                continue;
            }

            if (Contradicts(relation, declaredRelation, subject, entityTypeById))
            {
                mergedRejections.Add(new ProposalRejection(
                    ProposalElement.Relation,
                    relation.RelationName,
                    RejectionReason.ContradictsDeclaration,
                    $"relation \"{relation.RelationName}\" ({relation.FromEntityId} -> {relation.ToEntityId}) uses the declared name for ends the declaration ({declared.Subject.Value} -> {declaredRelation.Target.Value}) does not describe"));
                continue;
            }

            mergedRelations.Add(relation.WithBasis(ProposalBasis.Declared));
        }

        return new OntologyProposal(mergedEntities, mergedRelations, mergedRejections);
    }

    /// <summary>
    /// A declared relation runs from the declared subject to its declared target; an end that
    /// resolves to a differently typed entity is a contradiction. Both ends always resolve — the
    /// proposer leaves out every relation whose end is not a surviving entity before anything
    /// reaches this merge — so there is no unresolvable case to be lenient about here.
    /// </summary>
    private static bool Contradicts(RelationProposal relation, DeclaredRelation declaredRelation, string subject, Dictionary<string, string> entityTypeById)
        => entityTypeById[relation.FromEntityId] != subject
           || entityTypeById[relation.ToEntityId] != Normalize(declaredRelation.Target.Value);

    private static string Normalize(string name) => VocabularyName.Normalize(name);
}
