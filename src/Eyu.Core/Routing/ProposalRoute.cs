namespace Eyu.Core.Routing;

/// <summary>
/// Where a proposal goes once a caller's <see cref="RoutingPolicy"/> has been applied to its
/// confidence — the three tiers the README promises ("auto-apply, human review, or draft-only").
/// The route is advice to the caller; Eyu applies nothing itself.
/// </summary>
public enum ProposalRoute
{
    /// <summary>Confidence at or above the caller's auto-apply threshold: the caller may act without a human.</summary>
    AutoApply,

    /// <summary>Confidence in the caller's review band: present the proposal to a human for a decision.</summary>
    Review,

    /// <summary>Confidence below the caller's review threshold: keep only as a draft for a human to start from.</summary>
    DraftOnly,
}
