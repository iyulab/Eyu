namespace Eyu.Core.Proposals;

/// <summary>
/// The closed innate vocabulary (design rationale, §C): the small, domain-independent set of
/// entity types and relation names that <see cref="VocabularyOrigin.Innate"/> means. It anchors to
/// DOLCE — the entity types are its agentive and social endurants (Person, Organization), its
/// perdurants (Event, Action) and its spatial and temporal regions (Location, Time); the relation
/// names are its primitive relations of parthood, participation, and spatial and temporal
/// location.
/// <para>
/// A name is innate when it equals one of these after <em>lexical</em> comparison only (case and
/// separators ignored: <c>part_of</c> is <c>PartOf</c>). There is no synonym table: a model that
/// writes <c>Company</c> has used a domain word, not the innate <c>Organization</c>, and that choice
/// is what the tag reports. Mapping one onto the other would be the term normalization this
/// library does not perform (§B).
/// </para>
/// <para>
/// A proposer stamps origin from this vocabulary rather than asking a model to self-report it: a
/// model told only that origin is "Innate" or "Acquired" has no way to know which is which, and a
/// tag it guesses cannot carry the calibration distinction §D needs.
/// </para>
/// </summary>
public static class InnateVocabulary
{
    /// <summary>The innate entity types.</summary>
    public static IReadOnlyList<string> EntityTypes { get; } = ["Person", "Organization", "Event", "Action", "Location", "Time"];

    /// <summary>The innate relation names.</summary>
    public static IReadOnlyList<string> RelationNames { get; } = ["PartOf", "ParticipatesIn", "LocatedIn", "OccursAt"];

    private static readonly HashSet<string> NormalizedEntityTypes = [.. EntityTypes.Select(VocabularyName.Normalize)];
    private static readonly HashSet<string> NormalizedRelationNames = [.. RelationNames.Select(VocabularyName.Normalize)];

    /// <summary><see cref="VocabularyOrigin.Innate"/> when <paramref name="entityType"/> is an innate entity type, otherwise <see cref="VocabularyOrigin.Acquired"/>.</summary>
    public static VocabularyOrigin OfEntityType(string entityType)
        => NormalizedEntityTypes.Contains(VocabularyName.Normalize(entityType)) ? VocabularyOrigin.Innate : VocabularyOrigin.Acquired;

    /// <summary><see cref="VocabularyOrigin.Innate"/> when <paramref name="relationName"/> is an innate relation name, otherwise <see cref="VocabularyOrigin.Acquired"/>.</summary>
    public static VocabularyOrigin OfRelationName(string relationName)
        => NormalizedRelationNames.Contains(VocabularyName.Normalize(relationName)) ? VocabularyOrigin.Innate : VocabularyOrigin.Acquired;
}
