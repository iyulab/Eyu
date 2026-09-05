namespace Eyu.Core.Linkage;

/// <summary>
/// Tuning values for the Fellegi-Sunter pre-filter, threaded from
/// <see cref="Eyu.Core.Judgment.SinglePassOntologyProposer"/> through
/// <see cref="LinkagePipeline.Analyze"/> to <see cref="FieldComparator.Compare"/>,
/// <see cref="LinkageClassifier.Classify"/> and <see cref="FellegiSunterEstimator.Estimate"/>. All
/// defaults match what those methods already used, so passing no options changes nothing — a
/// caller only needs this once observed precision/recall from a live validation cycle calls for
/// different thresholds (design rationale §D of the base feature; decision 4 of the follow-up
/// design), or once literal field comparison misses notation-variant duplicates (see
/// <see cref="UseStringSimilarityComparator"/>).
/// </summary>
public sealed record LinkageOptions(
    double MatchThreshold = LinkageClassifier.DefaultMatchThreshold,
    double NonMatchThreshold = LinkageClassifier.DefaultNonMatchThreshold,
    int MaxIterations = FellegiSunterEstimator.DefaultMaxIterations,
    double ConvergenceTolerance = FellegiSunterEstimator.DefaultConvergenceTolerance,
    bool UseStringSimilarityComparator = false,
    double StringSimilarityAgreementThreshold = FieldComparator.DefaultStringSimilarityAgreementThreshold)
{
    public static readonly LinkageOptions Default = new();
}
