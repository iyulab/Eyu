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
/// <see cref="UseStringSimilarityComparator"/>), or because its records are not what the
/// pre-filter assumes they are (see <see cref="RecordsDenoteEntities"/>).
/// </summary>
/// <param name="RecordsDenoteEntities">
/// The pre-filter's premise: each record is one mention of one real-world entity, so two records
/// agreeing on their fields is evidence that they denote the same thing. That holds for a row, a
/// form submission, a directory entry. It does not hold for a document fragment — a text chunk with
/// a title and a path names many entities and denotes none — and there the premise inverts:
/// measured, chunks of one document agree on their metadata, the estimator reads that agreement as
/// identity, and the whole document is pre-linked as a single entity before the model sees it.
/// Pass <see langword="false"/> for such records and no pair is compared: every record is its own
/// singleton, the model receives no pre-linked groups and no gray-zone pairs, and the proposal's
/// grounding still cites record ids as before. Defaults to <see langword="true"/>.
/// </param>
public sealed record LinkageOptions(
    double MatchThreshold = LinkageClassifier.DefaultMatchThreshold,
    double NonMatchThreshold = LinkageClassifier.DefaultNonMatchThreshold,
    int MaxIterations = FellegiSunterEstimator.DefaultMaxIterations,
    double ConvergenceTolerance = FellegiSunterEstimator.DefaultConvergenceTolerance,
    bool UseStringSimilarityComparator = false,
    double StringSimilarityAgreementThreshold = FieldComparator.DefaultStringSimilarityAgreementThreshold,
    bool RecordsDenoteEntities = true)
{
    public static readonly LinkageOptions Default = new();

    /// <summary>
    /// Throws when a value could not be honoured: thresholds that do not order, an iteration
    /// count that would run EM zero times, a non-positive tolerance, or a similarity threshold
    /// outside [0, 1]. <see cref="LinkagePipeline.Analyze"/> calls this before it looks at the
    /// batch, so a bad option fails the same way whether the batch has one record or a thousand.
    /// </summary>
    public void Validate()
    {
        if (MatchThreshold <= NonMatchThreshold)
        {
            throw new ArgumentException(
                $"{nameof(MatchThreshold)} ({MatchThreshold}) must be greater than {nameof(NonMatchThreshold)} ({NonMatchThreshold}).",
                nameof(MatchThreshold));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(MaxIterations, 1, nameof(MaxIterations));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ConvergenceTolerance, nameof(ConvergenceTolerance));
        if (StringSimilarityAgreementThreshold is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(StringSimilarityAgreementThreshold), StringSimilarityAgreementThreshold, "Must be within [0, 1].");
        }
    }
}
