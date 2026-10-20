namespace Eyu.Core.Linkage;

/// <summary>
/// How <see cref="FieldComparator"/> decides that two values of one field agree. Every method first
/// folds both values the same way (<see cref="Eyu.Core.Primitives.TextForm.Fold"/>, surrounding
/// whitespace trimmed, case ignored); the similarity methods then compare the folded values and agree
/// at or above <see cref="LinkageOptions.StringSimilarityAgreementThreshold"/>.
/// </summary>
public enum FieldComparison
{
    /// <summary>The folded values are equal. The default.</summary>
    Exact,

    /// <summary>
    /// <see cref="JaroWinklerSimilarity"/> over the whole value, Hangul spelled out as its jamo — for
    /// short, name-like values whose variants differ by a letter, punctuation or spacing.
    /// </summary>
    JaroWinkler,

    /// <summary>
    /// <see cref="TokenSimilarity.Jaccard"/> over the values' word tokens — for values that share words
    /// in a different order or with extra words on one side (a product title, an address).
    /// </summary>
    TokenJaccard,

    /// <summary>
    /// <see cref="TokenSimilarity.MongeElkan"/> over the values' word tokens — each word matched to its
    /// closest counterpart by Jaro-Winkler, so word order and a misspelt word both count for little.
    /// </summary>
    MongeElkan,
}
