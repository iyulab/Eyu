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
/// not leave the declared subject, or does not reach the declared target — is dropped. The name is
/// declared fact; a proposal that uses the name for something else is inference dressed as
/// declaration, and inference does not win.</item>
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
    public static OntologyProposal Apply(DeclaredStructure? declared, IReadOnlyList<EntityProposal> entities, IReadOnlyList<RelationProposal> relations)
    {
        if (declared is null)
        {
            return new OntologyProposal(entities, relations);
        }

        var subject = Normalize(declared.Subject.Value);
        var mergedEntities = entities
            .Select(e => Normalize(e.EntityType) == subject ? e.WithBasis(ProposalBasis.Declared) : e)
            .ToList();
        var entityTypeById = mergedEntities.ToDictionary(e => e.EntityId, e => Normalize(e.EntityType), StringComparer.Ordinal);

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
                continue;
            }

            mergedRelations.Add(relation.WithBasis(ProposalBasis.Declared));
        }

        return new OntologyProposal(mergedEntities, mergedRelations);
    }

    /// <summary>
    /// A declared relation runs from the declared subject to its declared target; an end that
    /// resolves to a differently typed entity is a contradiction. Both ends always resolve — the
    /// proposer refuses a response whose relations name an entity it never proposed before anything
    /// reaches this merge — so there is no unresolvable case to be lenient about here.
    /// </summary>
    private static bool Contradicts(RelationProposal relation, DeclaredRelation declaredRelation, string subject, Dictionary<string, string> entityTypeById)
        => entityTypeById[relation.FromEntityId] != subject
           || entityTypeById[relation.ToEntityId] != Normalize(declaredRelation.Target.Value);

    private static string Normalize(string name)
        => new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
