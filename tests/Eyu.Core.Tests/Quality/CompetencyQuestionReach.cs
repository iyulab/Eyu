using Eyu.Core.Proposals;

namespace Eyu.Core.Tests.Quality;

/// <summary>
/// One question the proposed structure should be able to answer, with the concepts it needs in
/// order to reach it. Each concept may list alternatives separated by <c>|</c>: the proposal
/// invents its own vocabulary, so "the thing that was reported" can come back as an event, a
/// report or an occurrence, and a check that insisted on one spelling would measure wording
/// rather than structure.
/// </summary>
internal sealed record CompetencyQuestion
{
    /// <summary>
    /// The shortest alternative a concept may use. Matching is substring-based, so a two- or
    /// three-letter alternative is reached by ordinary type names that have nothing to do with the
    /// question — "at" is inside <c>Station</c> and <c>Operator</c>, "of" inside <c>Offsite</c>.
    /// A concept like that is reached by almost any proposal, which silently turns its question
    /// into one that asks less than the author wrote; the question then passes while the structure
    /// it was meant to test is missing. Refused at construction rather than reported later,
    /// because a catalog is written once and read every run.
    /// </summary>
    public const int MinimumAlternativeLength = 4;

    public CompetencyQuestion(string question, string[] requiredConcepts)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            throw new ArgumentException("A competency question needs its text.", nameof(question));
        }

        if (requiredConcepts.Length == 0)
        {
            throw new ArgumentException(
                $"Question \"{question}\" names no concept, so every proposal would reach it.",
                nameof(requiredConcepts));
        }

        foreach (var alternative in requiredConcepts.SelectMany(Alternatives))
        {
            if (alternative.Length < MinimumAlternativeLength)
            {
                throw new ArgumentException(
                    $"Question \"{question}\" uses the alternative \"{alternative}\", shorter than "
                    + $"{MinimumAlternativeLength} characters. Substring matching would reach it from "
                    + "unrelated type names, and the question would pass without testing anything.",
                    nameof(requiredConcepts));
            }
        }

        Question = question;
        RequiredConcepts = requiredConcepts;
    }

    public string Question { get; }

    public string[] RequiredConcepts { get; }

    internal static IEnumerable<string> Alternatives(string concept) =>
        concept.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>
/// Whether a proposal's structural vocabulary can reach a competency question. Deliberately
/// shallow: a name being present does not mean a query over the structure would return the right
/// answer, but a name being <em>absent</em> does mean no query can, and an unreachable question
/// points at a missing entity or relation. Same kind of signal as
/// <see cref="Eyu.Core.Grounding.GroundingOverlapCheck"/> — a heuristic to read per run, not a
/// verdict.
/// <para>
/// This lives in the test assembly rather than in <c>Eyu.Core</c>: the questions are a property of
/// a catalog domain, not of the library, and the measurement harness is the only consumer. It sits
/// outside <c>Live/Llm</c> so that the ordinary suite covers it — the live measurement that uses it
/// is excluded from a default <c>dotnet test</c>, and a rule nothing exercises is a rule nobody has
/// run.
/// </para>
/// </summary>
internal static class CompetencyQuestionReach
{
    /// <summary>
    /// The structural vocabulary a query would have to go through: the entity types the proposal
    /// invented, and the names it gave the relations between them. Claim text is left out on
    /// purpose — claims quote the records, so matching against them would report that the data
    /// contains the words of the question, which is true before any ontology is proposed at all.
    /// </summary>
    public static IReadOnlyList<string> Vocabulary(OntologyProposal proposal) =>
    [
        .. proposal.Entities.Select(entity => entity.EntityType)
            .Concat(proposal.Relations.Select(relation => relation.RelationName))
            .Select(term => term.ToLowerInvariant())
    ];

    /// <summary>
    /// True when every concept the question names is reachable in <paramref name="vocabulary"/>.
    /// A concept is reachable when any of its alternatives appears inside any vocabulary term, so
    /// <c>plant</c> is reached by <c>PowerPlant</c> as well as by <c>plant</c>.
    /// </summary>
    public static bool Reaches(CompetencyQuestion question, IReadOnlyList<string> vocabulary) =>
        question.RequiredConcepts.All(concept => IsReached(concept, vocabulary));

    private static bool IsReached(string concept, IReadOnlyList<string> vocabulary) =>
        CompetencyQuestion.Alternatives(concept)
            .Any(alternative => vocabulary.Any(term => term.Contains(alternative, StringComparison.Ordinal)));
}
