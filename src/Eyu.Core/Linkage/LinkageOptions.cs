namespace Eyu.Core.Linkage;

/// <summary>
/// Tuning values for the Fellegi-Sunter pre-filter, threaded from
/// <see cref="Eyu.Core.Judgment.SinglePassOntologyProposer"/> through
/// <see cref="LinkagePipeline.Analyze(IReadOnlyList{Eyu.Core.Records.RawRecord}, LinkageOptions)"/> to <see cref="FieldComparator.Compare"/>,
/// <see cref="LinkageClassifier.Classify"/> and <see cref="FellegiSunterEstimator.Estimate"/>. All
/// defaults match what those methods already used, so passing no options changes nothing — a
/// caller only needs this once observed precision/recall from a live validation cycle calls for
/// different thresholds (design rationale §D of the base feature; decision 4 of the follow-up
/// design), or once literal field comparison misses notation-variant duplicates (see
/// <see cref="Comparison"/>), or because its records are not what the
/// pre-filter assumes they are (see <see cref="RecordsDenoteEntities"/>).
/// </summary>
/// <param name="MatchThreshold">
/// The log-likelihood ratio at or above which a pair is a Match — the prior-free ratio, not the
/// posterior log-odds; see <see cref="LinkageClassifier"/> for the conversion and why.
/// </param>
/// <param name="NonMatchThreshold">
/// The log-likelihood ratio at or below which a pair is a NonMatch; between the two a pair is put
/// to the model. Same scale as <paramref name="MatchThreshold"/>.
/// </param>
/// <param name="MaxIterations">
/// The most expectation-maximization rounds the estimator runs when fitting the match and non-match
/// field probabilities; it stops earlier once they converge.
/// </param>
/// <param name="ConvergenceTolerance">
/// The fit is converged once no field's match or non-match agreement probability moves by this much
/// or more between two rounds. Must be positive.
/// </param>
/// <param name="Comparison">
/// How two values of a field are judged to agree (<see cref="FieldComparison"/>) when
/// <paramref name="FieldComparisons"/> names no comparison for that field: exact match (case- and
/// surrounding-whitespace-insensitive), Jaro-Winkler over the whole value for notation variants, or a
/// word-token method for values whose words differ in order or number. <see langword="null"/> — the
/// default — is <see cref="FieldComparison.Exact"/>.
/// </param>
/// <param name="FieldComparisons">
/// A comparison per field name (looked up with the dictionary's own comparer), overriding <paramref name="Comparison"/> for the fields it
/// names — a table whose title reads best as words, whose price must match exactly and whose maker's
/// name varies by a letter is compared each way at once. Defaults to none.
/// </param>
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
/// <param name="MaxGrayZonePairsInPrompt">
/// The most gray-zone pairs a proposer puts to the model in one prompt — pairs of the call's own records
/// and records set against a known entity, under one budget. Pairs grow with the square of the batch —
/// measured, 104 records produced 2,187 pairs and a prompt larger than a 131,072-token context, while the
/// records themselves took a fraction of it — and with the known population: a record with a common name
/// and few other fields is gray against every known entity sharing it. Past the cap the pairs with the
/// strongest prior evidence toward the same entity are kept (in their usual order) and the rest are
/// left out; the proposal reports how many (<see cref="Eyu.Core.Proposals.LinkageReport.GrayZonePairsOmitted"/>).
/// A pair left out is not judged "different" — the model still sees both records and may still merge
/// them — it is only not singled out as needing judgment. Zero puts no pair to the model. Defaults to
/// <see cref="DefaultMaxGrayZonePairsInPrompt"/>.
/// </param>
/// <param name="BlockingRules">
/// Which pairs are compared at all. Without rules every pair is — the cost grows with the square of a
/// batch, and with the whole known population for each record matched against it. Each rule is a set
/// of field names; two records form a pair when, for at least one rule, every field it names is present
/// on both and equal as <see cref="FieldComparator"/> compares exactly (case, surrounding whitespace and
/// Unicode form aside). Rules are alternatives, so a pair one rule misses — a misspelt name — can be
/// caught by another (the same birth date and school). A pair no rule reaches is not judged different;
/// it is not compared. Blocking narrows what EM sees too, so when the pairs a call fits on are blocked,
/// <c>u</c> is better measured over the unblocked population
/// (<see cref="LinkagePipeline.EstimateNonMatchAgreement"/>, which ignores these rules) and supplied.
/// Defaults to none.
/// </param>
public sealed record LinkageOptions(
    double MatchThreshold = LinkageClassifier.DefaultMatchThreshold,
    double NonMatchThreshold = LinkageClassifier.DefaultNonMatchThreshold,
    int MaxIterations = FellegiSunterEstimator.DefaultMaxIterations,
    double ConvergenceTolerance = FellegiSunterEstimator.DefaultConvergenceTolerance,
    FieldComparison? Comparison = null,
    IReadOnlyDictionary<string, FieldComparison>? FieldComparisons = null,
    bool RecordsDenoteEntities = true,
    int MaxGrayZonePairsInPrompt = LinkageOptions.DefaultMaxGrayZonePairsInPrompt,
    IReadOnlyList<IReadOnlyList<string>>? BlockingRules = null)
{
    /// <summary>The default for <see cref="MaxGrayZonePairsInPrompt"/>.</summary>
    public const int DefaultMaxGrayZonePairsInPrompt = 200;

    public static readonly LinkageOptions Default = new();

    /// <summary>
    /// The comparison <see cref="FieldComparator"/> applies to <paramref name="fieldName"/>: its entry in
    /// <see cref="FieldComparisons"/>, else <see cref="Comparison"/>, else <see cref="FieldComparison.Exact"/>.
    /// </summary>
    public FieldComparison ComparisonFor(string fieldName)
    {
        ArgumentNullException.ThrowIfNull(fieldName);
        return FieldComparisons is not null && FieldComparisons.TryGetValue(fieldName, out var perField)
            ? perField
            : Comparison ?? FieldComparison.Exact;
    }

    /// <summary>
    /// Throws when a value could not be honoured: thresholds that do not order, an iteration
    /// count that would run EM zero times, a non-positive tolerance, a comparison with an undefined method or a
    /// threshold outside [0, 1], a per-field comparison without a field name, a negative gray-zone pair cap, or a blocking rule that names no field. <see cref="LinkagePipeline.Analyze(IReadOnlyList{Eyu.Core.Records.RawRecord}, LinkageOptions)"/> calls this before it looks at the
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

        Comparison?.Validate();
        foreach (var (field, comparison) in FieldComparisons ?? new Dictionary<string, FieldComparison>())
        {
            if (string.IsNullOrWhiteSpace(field) || comparison is null)
            {
                throw new ArgumentException("Every per-field comparison names a field and a comparison.", nameof(FieldComparisons));
            }

            comparison.Validate();
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(MaxIterations, 1, nameof(MaxIterations));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ConvergenceTolerance, nameof(ConvergenceTolerance));
        ArgumentOutOfRangeException.ThrowIfNegative(MaxGrayZonePairsInPrompt, nameof(MaxGrayZonePairsInPrompt));

        foreach (var rule in BlockingRules ?? [])
        {
            if (rule is null || rule.Count == 0 || rule.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException("Every blocking rule names at least one field, and no blank one.", nameof(BlockingRules));
            }
        }
    }
}
