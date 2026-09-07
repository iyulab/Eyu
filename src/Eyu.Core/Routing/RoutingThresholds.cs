namespace Eyu.Core.Routing;

/// <summary>
/// One calibration regime's cut-offs: confidence at or above <paramref name="AutoApplyAt"/> routes
/// <see cref="ProposalRoute.AutoApply"/>, at or above <paramref name="ReviewAt"/> routes
/// <see cref="ProposalRoute.Review"/>, anything lower <see cref="ProposalRoute.DraftOnly"/>. There is
/// deliberately no default: self-reported confidence is uncalibrated until a caller has tuned these
/// against observed precision/recall on its own data (design rationale §D), so a value that
/// "works out of the box" would be a promise Eyu cannot keep.
/// </summary>
/// <param name="AutoApplyAt">Lower bound of the auto-apply tier, in [0, 1].</param>
/// <param name="ReviewAt">Lower bound of the review tier, in [0, <paramref name="AutoApplyAt"/>]. Equal to <paramref name="AutoApplyAt"/> collapses the review tier.</param>
public sealed record RoutingThresholds(double AutoApplyAt, double ReviewAt)
{
    /// <summary>Throws when either bound is outside [0, 1] or the tiers do not order.</summary>
    public void Validate()
    {
        if (AutoApplyAt is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(AutoApplyAt), AutoApplyAt, "Must be within [0, 1].");
        }

        if (ReviewAt is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(ReviewAt), ReviewAt, "Must be within [0, 1].");
        }

        if (ReviewAt > AutoApplyAt)
        {
            throw new ArgumentException(
                $"{nameof(ReviewAt)} ({ReviewAt}) must not exceed {nameof(AutoApplyAt)} ({AutoApplyAt}).",
                nameof(ReviewAt));
        }
    }

    /// <summary>The tier this regime assigns to <paramref name="confidence"/>; both lower bounds are inclusive.</summary>
    public ProposalRoute Route(double confidence) =>
        confidence >= AutoApplyAt ? ProposalRoute.AutoApply
        : confidence >= ReviewAt ? ProposalRoute.Review
        : ProposalRoute.DraftOnly;
}
