using Eyu.Core.Proposals;

namespace Eyu.Core.Routing;

/// <summary>
/// The caller-defined thresholds that turn a proposal's confidence into a <see cref="ProposalRoute"/>,
/// kept per <see cref="VocabularyOrigin"/> because innate and acquired proposals do not share a
/// calibration curve (design rationale §C) — one pooled cut-off would silently tune the wrong regime.
/// Construct only via <see cref="Create"/> or <see cref="Uniform"/>, both of which validate up front so
/// a bad threshold fails before any proposal is routed.
/// </summary>
public sealed record RoutingPolicy
{
    public RoutingThresholds Innate { get; }
    public RoutingThresholds Acquired { get; }

    private RoutingPolicy(RoutingThresholds innate, RoutingThresholds acquired)
    {
        Innate = innate;
        Acquired = acquired;
    }

    /// <summary>Separate thresholds per origin — the shape §C asks a calibrating caller to reach for.</summary>
    public static RoutingPolicy Create(RoutingThresholds innate, RoutingThresholds acquired)
    {
        ArgumentNullException.ThrowIfNull(innate);
        ArgumentNullException.ThrowIfNull(acquired);
        innate.Validate();
        acquired.Validate();
        return new RoutingPolicy(innate, acquired);
    }

    /// <summary>
    /// The same thresholds for both origins — a starting point before a caller has enough held-out
    /// cases to tune each regime separately, not a statement that the regimes agree.
    /// </summary>
    public static RoutingPolicy Uniform(RoutingThresholds thresholds) => Create(thresholds, thresholds);

    /// <summary>The thresholds that govern proposals of <paramref name="origin"/>.</summary>
    public RoutingThresholds For(VocabularyOrigin origin) => origin switch
    {
        VocabularyOrigin.Innate => Innate,
        VocabularyOrigin.Acquired => Acquired,
        _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, "Unknown vocabulary origin."),
    };
}
