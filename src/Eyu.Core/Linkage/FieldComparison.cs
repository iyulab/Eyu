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
/// <param name="AgreementThreshold">
/// The similarity at or above which two values agree, within [0, 1]; ignored by
/// <see cref="FieldComparisonMethod.Exact"/>. The methods' scales differ — a Jaccard of 0.9 asks for
/// nearly the same words, a Jaro-Winkler of 0.9 for nearly the same spelling.
/// </param>
public sealed record FieldComparison(FieldComparisonMethod Method, double AgreementThreshold)
{
    /// <summary>
    /// The threshold <see cref="JaroWinkler"/> and <see cref="MongeElkan"/> use unless given another — a
    /// Jaro-Winkler score, which both are on.
    /// </summary>
    public const double DefaultJaroWinklerThreshold = 0.90;

    /// <summary>The folded values are equal. The default.</summary>
    public static FieldComparison Exact { get; } = new(FieldComparisonMethod.Exact, 1.0);

    /// <summary>
    /// <see cref="JaroWinklerSimilarity"/> over the whole value, Hangul spelled out as its jamo, at or
    /// above <paramref name="agreementThreshold"/>.
    /// </summary>
    public static FieldComparison JaroWinkler(double agreementThreshold = DefaultJaroWinklerThreshold) =>
        new(FieldComparisonMethod.JaroWinkler, agreementThreshold);

    /// <summary>
    /// <see cref="TokenSimilarity.Jaccard"/> over the values' words at or above
    /// <paramref name="agreementThreshold"/> — the share of distinct words the two values have in common.
    /// No default: the scale is not Jaro-Winkler's, so the caller says how many words must be shared.
    /// </summary>
    public static FieldComparison TokenJaccard(double agreementThreshold) =>
        new(FieldComparisonMethod.TokenJaccard, agreementThreshold);

    /// <summary>
    /// <see cref="TokenSimilarity.MongeElkan"/> over the values' words at or above
    /// <paramref name="agreementThreshold"/> — each word matched to its closest counterpart by
    /// Jaro-Winkler, so word order and a misspelt word both count for little.
    /// </summary>
    public static FieldComparison MongeElkan(double agreementThreshold = DefaultJaroWinklerThreshold) =>
        new(FieldComparisonMethod.MongeElkan, agreementThreshold);

    /// <summary>Throws when the method is not defined or the threshold is outside [0, 1].</summary>
    public void Validate()
    {
        if (!Enum.IsDefined(Method))
        {
            throw new ArgumentOutOfRangeException(nameof(Method), Method, "Not a defined comparison method.");
        }

        if (AgreementThreshold is < 0.0 or > 1.0 || double.IsNaN(AgreementThreshold))
        {
            throw new ArgumentOutOfRangeException(nameof(AgreementThreshold), AgreementThreshold, "Must be within [0, 1].");
        }
    }

    /// <summary>Whether two folded, trimmed values agree under this comparison.</summary>
    internal bool Agrees(string a, string b) => Method switch
    {
        FieldComparisonMethod.Exact => string.Equals(a, b, StringComparison.OrdinalIgnoreCase),
        _ => Similarity(a.ToUpperInvariant(), b.ToUpperInvariant()) >= AgreementThreshold,
    };

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
