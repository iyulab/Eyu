using Eyu.Core.Declared;
using Eyu.Core.Primitives;

namespace Eyu.Core.Tests.Quality;

/// <summary>
/// One thing a form declares — a field, or a relation carried by a field. A declaration is a
/// list of these in <em>form order</em>: each field followed by the relation it carries, so that
/// cutting the list short removes trailing sections of the form rather than, say, every relation
/// at once. The order matters because only relation names and target types enter a proposal's
/// structural vocabulary (<see cref="CompetencyQuestionReach.Vocabulary"/>); a ladder that listed
/// all fields first would stay flat until its last rungs and measure the ordering, not the
/// completeness.
/// </summary>
internal abstract record DeclarationItem
{
    private DeclarationItem()
    {
    }

    public sealed record Field(DeclaredField Declared) : DeclarationItem;

    public sealed record Relation(DeclaredRelation Declared) : DeclarationItem;
}

/// <summary>
/// One rung of a <see cref="DeclarationLadder"/>: how much of the full declaration was kept, and
/// the structure that keeping it produces. <see cref="Structure"/> is <c>null</c> at completeness
/// 0 — nothing declared at all, which is the baseline the quality measurement already runs.
/// </summary>
internal sealed record DeclarationLevel(
    double Completeness,
    int ItemsKept,
    int ItemsTotal,
    DeclaredStructure? Structure);

/// <summary>
/// The full declaration a form would make for one catalog case, and the graded ablation of it.
/// A schema-completeness–recall correlation needs the same declaration at several completeness
/// levels; this type produces them as prefixes of the form-ordered item list, so that level
/// <c>k</c> of <c>n</c> keeps the first <c>round(k/n · items)</c> items. Completeness is the kept
/// fraction — the unweighted form of a coverage index (every declared item weighs 1), which is
/// what a coverage index reduces to when no field-importance weights are known.
/// </summary>
internal sealed class DeclarationLadder
{
    public DeclarationLadder(SubjectRef subject, IReadOnlyList<DeclarationItem> items)
    {
        if (items.Count == 0)
        {
            throw new ArgumentException("A declaration ladder needs at least one item; an empty form has no completeness to grade.", nameof(items));
        }

        Subject = subject;
        Items = items;
    }

    public SubjectRef Subject { get; }

    public IReadOnlyList<DeclarationItem> Items { get; }

    /// <summary>
    /// The rungs from nothing declared (completeness 0, <c>null</c> structure) to the full
    /// declaration (completeness 1), <paramref name="steps"/> + 1 of them.
    /// </summary>
    public IReadOnlyList<DeclarationLevel> Levels(int steps)
    {
        if (steps < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(steps), steps, "A ladder needs at least one step between nothing and everything.");
        }

        return
        [
            .. Enumerable.Range(0, steps + 1).Select(step =>
            {
                var completeness = (double)step / steps;
                var kept = (int)Math.Round(completeness * Items.Count, MidpointRounding.AwayFromZero);
                return new DeclarationLevel(completeness, kept, Items.Count, Prefix(kept));
            }),
        ];
    }

    /// <summary>
    /// The structural vocabulary a declaration itself contributes, in the same shape
    /// <see cref="CompetencyQuestionReach.Vocabulary"/> reads from a proposal: the subject, each
    /// declared relation's name, and each relation's target type. Fields are left out because a
    /// declared field never enters a proposal's vocabulary on its own. A question this vocabulary
    /// reaches is one the declaration <em>names</em>; whether the proposal reaches it is what the
    /// live measurement observes.
    /// </summary>
    public static IReadOnlyList<string> DeclaredVocabulary(DeclaredStructure? structure)
    {
        if (structure is null)
        {
            return [];
        }

        return
        [
            structure.Subject.Value.ToLowerInvariant(),
            .. structure.Relations.SelectMany(relation => new[] { relation.Name, relation.Target.Value })
                .Select(term => term.ToLowerInvariant()),
        ];
    }

    private DeclaredStructure? Prefix(int kept)
    {
        if (kept == 0)
        {
            return null;
        }

        var head = Items.Take(kept).ToList();
        return new DeclaredStructure(
            Subject,
            [.. head.OfType<DeclarationItem.Field>().Select(item => item.Declared)],
            [.. head.OfType<DeclarationItem.Relation>().Select(item => item.Declared)]);
    }
}
