using Eyu.Core.Primitives;

namespace Eyu.Core.Linkage;

/// <summary>
/// How <see cref="FieldComparator"/> decides that two values of a field agree: a method and, for the
/// similarity methods, the score at or above which values agree. Every method first folds both values
/// the same way (<see cref="TextForm.Fold"/>, surrounding whitespace trimmed, case ignored). An exact
/// match scores 1 under every method and clears any threshold, so each similarity method agrees
/// wherever <see cref="Exact"/> does, and on more.
/// </summary>
/// <remarks>
/// Choose by what the field's values look like. Whole-value methods suit short values whose variants
/// differ by a letter, punctuation or spacing (a name, a code); word-token methods suit values whose
/// words move or are added (a product title, an address); a value that must match to the letter (an
/// amount, an identifier) is best left exact. <see cref="LinkageOptions.FieldComparisons"/> sets a
/// method per field.
/// </remarks>
/// <param name="Method">The comparison method.</param>
/// <param name="AgreementThresholds">
/// The similarities at or above which two values agree, highest first, each within [0, 1] — the levels of
/// agreement (<see cref="FieldAgreementLevel"/>): a pair that clears <c>k</c> of them lands on rank
/// <c>k</c>, one that clears none disagrees. One threshold is plain agree/disagree; several, as in
/// <c>[1.0, 0.92, 0.88]</c>, give a near-miss its own weight between a match and a disagreement, which
/// the estimator fits per level (the Fellegi–Sunter general form, Splink's comparison levels). A threshold
/// of 1.0 is the exact level: every method scores 1 only for equal values. Empty for
/// <see cref="FieldComparisonMethod.Exact"/>, which has two levels. The methods' scales differ — a Jaccard
/// of 0.9 asks for nearly the same words, a Jaro-Winkler of 0.9 for nearly the same spelling.
/// </param>
public sealed record FieldComparison(FieldComparisonMethod Method, IReadOnlyList<double> AgreementThresholds)
{
    /// <summary>
    /// The threshold <see cref="JaroWinkler"/> and <see cref="MongeElkan"/> use unless given others — a
    /// Jaro-Winkler score, which both are on.
    /// </summary>
    public const double DefaultJaroWinklerThreshold = 0.90;

    /// <summary>The folded values are equal. The default.</summary>
    public static FieldComparison Exact { get; } = new(FieldComparisonMethod.Exact, []);

    /// <summary>
    /// <see cref="JaroWinklerSimilarity"/> over the whole value, Hangul spelled out as its jamo, at or
    /// above each of <paramref name="agreementThresholds"/> (highest first) — <see cref="DefaultJaroWinklerThreshold"/>
    /// alone when none is given.
    /// </summary>
    public static FieldComparison JaroWinkler(params double[] agreementThresholds) =>
        new(FieldComparisonMethod.JaroWinkler, agreementThresholds.Length == 0 ? [DefaultJaroWinklerThreshold] : [.. agreementThresholds]);

    /// <summary>
    /// <see cref="TokenSimilarity.Jaccard"/> over the values' words at or above
    /// <paramref name="agreementThreshold"/> and each of <paramref name="lowerThresholds"/> — the share of
    /// distinct words the two values have in common. No default: the scale is not Jaro-Winkler's, so the
    /// caller says how many words must be shared.
    /// </summary>
    public static FieldComparison TokenJaccard(double agreementThreshold, params double[] lowerThresholds) =>
        new(FieldComparisonMethod.TokenJaccard, [agreementThreshold, .. lowerThresholds]);

    /// <summary>
    /// <see cref="TokenSimilarity.MongeElkan"/> over the values' words at or above each of
    /// <paramref name="agreementThresholds"/> (highest first; <see cref="DefaultJaroWinklerThreshold"/> alone
    /// when none is given) — each word matched to its closest counterpart by Jaro-Winkler, so word order and
    /// a misspelt word both count for little.
    /// </summary>
    public static FieldComparison MongeElkan(params double[] agreementThresholds) =>
        new(FieldComparisonMethod.MongeElkan, agreementThresholds.Length == 0 ? [DefaultJaroWinklerThreshold] : [.. agreementThresholds]);

    /// <summary>How many levels this comparison sorts a pair into, disagreement included.</summary>
    public int LevelCount => Method == FieldComparisonMethod.Exact ? 2 : AgreementThresholds.Count + 1;

    /// <summary>
    /// Throws when the method is not defined, a similarity method has no threshold, a threshold is outside
    /// [0, 1], or the thresholds are not strictly descending.
    /// </summary>
    public void Validate()
    {
        if (!Enum.IsDefined(Method))
        {
            throw new ArgumentOutOfRangeException(nameof(Method), Method, "Not a defined comparison method.");
        }

        ArgumentNullException.ThrowIfNull(AgreementThresholds);
        if (Method != FieldComparisonMethod.Exact && AgreementThresholds.Count == 0)
        {
            throw new ArgumentException($"{Method} needs at least one agreement threshold.", nameof(AgreementThresholds));
        }

        for (var i = 0; i < AgreementThresholds.Count; i++)
        {
            var threshold = AgreementThresholds[i];
            if (threshold is < 0.0 or > 1.0 || double.IsNaN(threshold))
            {
                throw new ArgumentOutOfRangeException(nameof(AgreementThresholds), threshold, "Every threshold must be within [0, 1].");
            }

            if (i > 0 && threshold >= AgreementThresholds[i - 1])
            {
                throw new ArgumentException("Agreement thresholds must be strictly descending — highest first.", nameof(AgreementThresholds));
            }
        }
    }

    /// <summary>Equal when the method and every threshold are — the thresholds compared by value, in order.</summary>
    public bool Equals(FieldComparison? other) =>
        other is not null && Method == other.Method && AgreementThresholds.SequenceEqual(other.AgreementThresholds);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Method);
        foreach (var threshold in AgreementThresholds)
        {
            hash.Add(threshold);
        }

        return hash.ToHashCode();
    }

    /// <summary>The level two folded, trimmed values reach under this comparison.</summary>
    internal FieldAgreementLevel Level(string a, string b)
    {
        if (Method == FieldComparisonMethod.Exact)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase) ? FieldAgreementLevel.Agree : FieldAgreementLevel.Disagree;
        }

        // A value clearing a threshold clears every lower one, so the rank is how many it clears.
        var similarity = Similarity(a.ToUpperInvariant(), b.ToUpperInvariant());
        return new FieldAgreementLevel(AgreementThresholds.Count(threshold => similarity >= threshold), LevelCount);
    }

    private double Similarity(string a, string b) => Method switch
    {
        FieldComparisonMethod.JaroWinkler => JaroWinklerSimilarity.Compute(TextForm.DecomposeHangul(a), TextForm.DecomposeHangul(b)),
        FieldComparisonMethod.TokenJaccard => TokenSimilarity.Jaccard(a, b),
        FieldComparisonMethod.MongeElkan => TokenSimilarity.MongeElkan(a, b),
        _ => throw new InvalidOperationException($"{Method} is not a similarity method."),
    };
}

/// <summary>The methods a <see cref="FieldComparison"/> compares values by.</summary>
public enum FieldComparisonMethod
{
    /// <summary>The folded values are equal.</summary>
    Exact,

    /// <summary><see cref="JaroWinklerSimilarity"/> over the whole value, Hangul spelled out as its jamo.</summary>
    JaroWinkler,

    /// <summary><see cref="TokenSimilarity.Jaccard"/> over the values' words.</summary>
    TokenJaccard,

    /// <summary><see cref="TokenSimilarity.MongeElkan"/> over the values' words.</summary>
    MongeElkan,
}
