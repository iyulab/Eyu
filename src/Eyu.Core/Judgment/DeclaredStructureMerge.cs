using Eyu.Core.Declared;
using Eyu.Core.Proposals;

namespace Eyu.Core.Judgment;

/// <summary>
/// The deterministic half of "Declared always wins". The prompt tells the model that declared
/// structure is authoritative, but a sentence in a prompt is a request, not a guarantee — what a
/// caller can rely on is decided here, after the model has answered, without a second model call:
/// <list type="bullet">
/// <item>An entity whose type is a declared subject, and a relation whose name is a declared
/// relation, are stamped <see cref="ProposalBasis.Declared"/> — whatever the model said about
/// itself. Everything else is <see cref="ProposalBasis.Inferred"/>.</item>
/// <item>A relation proposed under a declared name whose ends match no declaration of that name —
/// it does not leave a subject that declares the name, or does not reach that declaration's target —
/// is left out and reported as <see cref="RejectionReason.ContradictsDeclaration"/>. The name is
/// declared fact; a proposal that uses the name for something else is inference dressed as
/// declaration, and inference does not win. It is reported rather than silently dropped so a caller
/// measuring the model sees it. Several subjects may declare the same relation name (a component and
/// a department can both be <c>PartOf</c> something), and a relation fits if it matches any one of
/// those declarations.</item>
/// </list>
/// Nothing beyond the declarations is filtered: a type or relation no declaration names is returned
/// as inferred, because a declaration is a floor, not a ceiling. Nor is anything renamed — a model
/// that writes <c>Company</c> where <c>Organization</c> is declared has proposed an undeclared type,
/// and mapping one onto the other would be the term normalization this library does not perform
/// (design rationale §B).
/// Type names are compared leniently (case and separators ignored: <c>WorkOrder</c>,
/// <c>work_order</c> and <c>work-order</c> are one name) because the model writes them and the
/// caller declares them in whatever convention each has. Nothing here touches
/// <see cref="VocabularyOrigin"/> or confidence: which vocabulary a type came from, and how sure
/// the model is about the <em>instance</em>, are different questions from whether the type was
/// declared.
/// </summary>
internal static class DeclaredStructureMerge
{
    /// <summary>
    /// Refuses declarations no merge could honor — two of the same subject, where neither could be
    /// the authority — before the model is called, so a caller error does not cost a model call.
    /// </summary>
    public static void EnsureDistinctSubjects(IReadOnlyList<DeclaredStructure> declared)
    {
        ArgumentNullException.ThrowIfNull(declared);

        var repeated = declared
            .GroupBy(d => Normalize(d.Subject.Value), StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => string.Join(", ", g.Select(d => $"\"{d.Subject.Value}\"")))
            .ToList();
        if (repeated.Count > 0)
        {
            throw new ArgumentException(
                $"Each subject may be declared once, but these declarations name the same subject: {string.Join("; ", repeated)}. Merge them into one declaration.",
                nameof(declared));
        }
    }

    public static OntologyProposal Apply(IReadOnlyList<DeclaredStructure> declared, IReadOnlyList<EntityProposal> entities, IReadOnlyList<RelationProposal> relations, IReadOnlyList<ProposalRejection> rejections)
    {
        if (declared.Count == 0)
        {
            return new OntologyProposal(entities, relations, rejections);
        }

        var subjects = declared.Select(d => Normalize(d.Subject.Value)).ToHashSet(StringComparer.Ordinal);
        var mergedEntities = entities
            .Select(e => subjects.Contains(Normalize(e.EntityType)) ? e.WithBasis(ProposalBasis.Declared) : e)
            .ToList();
        var entityTypeById = mergedEntities.ToDictionary(e => e.EntityId, e => Normalize(e.EntityType), StringComparer.Ordinal);

        var declaredEndsByName = declared
            .SelectMany(d => d.Relations.Select(r => (Name: Normalize(r.Name), Ends: (From: Normalize(d.Subject.Value), To: Normalize(r.Target.Value)))))
            .ToLookup(r => r.Name, r => r.Ends, StringComparer.Ordinal);

        var mergedRejections = new List<ProposalRejection>(rejections);
        var mergedRelations = new List<RelationProposal>(relations.Count);
        foreach (var relation in relations)
        {
            var name = Normalize(relation.RelationName);
            if (!declaredEndsByName.Contains(name))
            {
                mergedRelations.Add(relation);
                continue;
            }

            // Both ends always resolve: the proposer leaves out every relation whose end is not a
            // surviving entity before anything reaches this merge, so there is no unresolvable case
            // to be lenient about here.
            var ends = (From: entityTypeById[relation.FromEntityId], To: entityTypeById[relation.ToEntityId]);
            if (!declaredEndsByName[name].Contains(ends))
            {
                mergedRejections.Add(new ProposalRejection(
                    ProposalElement.Relation,
                    relation.RelationName,
                    RejectionReason.ContradictsDeclaration,
                    $"relation \"{relation.RelationName}\" ({relation.FromEntityId} -> {relation.ToEntityId}) uses the declared name for ends no declaration of it describes ({DescribeDeclaredEnds(declared, name)})"));
                continue;
            }

            mergedRelations.Add(relation.WithBasis(ProposalBasis.Declared));
        }

        return new OntologyProposal(mergedEntities, mergedRelations, mergedRejections);
    }

    /// <summary>Every declared <c>subject -> target</c> for a relation name, as the caller wrote them.</summary>
    private static string DescribeDeclaredEnds(IReadOnlyList<DeclaredStructure> declared, string normalizedName)
        => string.Join(", ", declared.SelectMany(d => d.Relations
            .Where(r => Normalize(r.Name) == normalizedName)
            .Select(r => $"{d.Subject.Value} -> {r.Target.Value}")));

    private static string Normalize(string name) => VocabularyName.Normalize(name);
}
