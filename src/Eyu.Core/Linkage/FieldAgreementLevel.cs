namespace Eyu.Core.Linkage;

/// <summary>
/// How closely two records' values for one field agree, on the scale of the comparison that judged them
/// (<see cref="FieldComparison"/>): <see cref="Rank"/> 0 is disagreement and each rank above it is
/// closer, up to <c><see cref="Count"/> − 1</c>. A comparison with one threshold, or exact matching,
/// has two levels — <see cref="Disagree"/> and <see cref="Agree"/>; one with thresholds
/// <c>[1.0, 0.92, 0.88]</c> has four, and an exact match lands on the top one. Each level carries its own
/// match and non-match probability (<see cref="FieldLinkageParameters"/>), so a near-miss weighs between a
/// match and a disagreement rather than being forced to one of them. A field missing or blank on either
/// side never produces a level — see <see cref="FieldComparator.Compare"/>.
/// </summary>
public readonly record struct FieldAgreementLevel
{
    /// <param name="Rank">0 for disagreement, higher for closer; below <paramref name="Count"/>.</param>
    /// <param name="Count">How many levels the comparison has; at least 2.</param>
    public FieldAgreementLevel(int Rank, int Count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(Count, 2);
        ArgumentOutOfRangeException.ThrowIfNegative(Rank);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(Rank, Count);
        this.Rank = Rank;
        this.Count = Count;
    }

    /// <summary>0 for disagreement, higher for closer.</summary>
    public int Rank { get; }

    /// <summary>How many levels the comparison has.</summary>
    public int Count { get; }

    /// <summary>Disagreement on a two-level comparison.</summary>
    public static FieldAgreementLevel Disagree { get; } = new(0, 2);

    /// <summary>Agreement on a two-level comparison.</summary>
    public static FieldAgreementLevel Agree { get; } = new(1, 2);

    /// <summary>True for every level above disagreement.</summary>
    public bool IsAgreement => Rank > 0;

    /// <summary>True for the closest level of the comparison.</summary>
    public bool IsTop => Rank == Count - 1;
}
