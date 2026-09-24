namespace Eyu.Core.Linkage;

/// <summary>
/// Why a <see cref="LinkageErrorRateEstimate"/> should not be read as a measurement. Any set bit
/// means a precondition of the estimate did not hold for the batch; the numbers are still
/// reported, because a caller comparing two runs of the same batch wants them either way, but
/// <see cref="LinkageErrorRateEstimate.IsReliable"/> is false.
/// </summary>
[Flags]
public enum LinkageErrorRateCaveat
{
    None = 0,

    /// <summary>
    /// The parameters are the heuristic default, not an EM estimate: the posteriors the estimate is
    /// built from were assumed, not learned from this batch.
    /// </summary>
    HeuristicParameters = 1 << 0,

    /// <summary>EM ran but did not converge within its iteration budget.</summary>
    NotConverged = 1 << 1,

    /// <summary>
    /// Fewer pairs than <see cref="FellegiSunterEstimator.MinimumPairsForEmEstimation"/> — too few
    /// to estimate a mixture from at all.
    /// </summary>
    TooFewPairs = 1 << 2,

    /// <summary>
    /// The estimator had to swap the mixture components back (see
    /// <see cref="FellegiSunterEstimator.WithMatchComponentFirst"/>); the fit is usable but was
    /// not clean.
    /// </summary>
    LabelsSwapped = 1 << 3,

    /// <summary>
    /// The two mixture components are not well separated: too many pairs sit at an ambiguous
    /// posterior, so the expected counts the rates are built from are mostly guesswork.
    /// </summary>
    PoorSeparation = 1 << 4,

    /// <summary>
    /// Fewer than <see cref="FellegiSunterEstimator.MinimumFieldsForIdentification"/> fields were
    /// compared, so the mixture was not identifiable: the fitted prior, <c>m</c> and <c>u</c> are one
    /// fit among many that explain the pairs equally well, and the posteriors and rates built on them
    /// are arbitrary rather than estimated. Records of one short text field are the usual case —
    /// measured, such a batch "converged" with every pair left to the model and both rates 0.
    /// </summary>
    TooFewFields = 1 << 5,
}

/// <summary>
/// An estimate of the pre-filter's own error rates on one batch, made <em>without labels</em> from
/// the EM model's posteriors (Winkler's approach: the fitted mixture says how likely each pair is
/// a match, so it also says how many of the pairs called Match are expected not to be, and how
/// many called NonMatch are expected to be).
/// <para>
/// <paramref name="FalseMatchRate"/> is the expected share of true non-matches that were
/// classified <see cref="LinkageClassification.Match"/>; <paramref name="FalseNonMatchRate"/> the
/// expected share of true matches classified <see cref="LinkageClassification.NonMatch"/>;
/// <paramref name="GrayZoneShare"/> the share of pairs deferred to the model. These are model-based
/// expectations, not observed counts — they are exactly as good as the mixture fit, which is what
/// <paramref name="Caveats"/> reports. It does not <em>measure</em> the batch's accuracy; measuring
/// it takes a clerical review of a sample, and this estimate is what says whether that review is
/// worth running and where to point it. One limit the caveats cannot see: the mixture assumes the
/// compared fields are independent, and a caveat-free fit on fields that move together (a suburb
/// and its postcode) was measured against a labeled benchmark off by a factor of five or more in
/// both rates (<c>docs/linkage-benchmark.md</c>) — read it as an order of magnitude.
/// </para>
/// </summary>
public sealed record LinkageErrorRateEstimate(
    double FalseMatchRate,
    double FalseNonMatchRate,
    double GrayZoneShare,
    double ExpectedMatches,
    double ExpectedNonMatches,
    LinkageErrorRateCaveat Caveats)
{
    /// <summary>True when every precondition of the estimate held for this batch.</summary>
    public bool IsReliable => Caveats == LinkageErrorRateCaveat.None;
}

/// <summary>
/// Computes <see cref="LinkageErrorRateEstimate"/> from the classified pairs and the parameters
/// that classified them.
/// </summary>
public static class LinkageErrorRateEstimator
{
    /// <summary>
    /// A pair whose posterior match probability lies strictly inside this band is "ambiguous": the
    /// model does not really know which component it belongs to.
    /// </summary>
    public const double AmbiguousPosteriorLow = 0.2;
    public const double AmbiguousPosteriorHigh = 0.8;

    /// <summary>
    /// When at least this share of pairs is ambiguous, the mixture is not separating the batch and
    /// <see cref="LinkageErrorRateCaveat.PoorSeparation"/> is set.
    /// </summary>
    public const double MaxAmbiguousShare = 0.5;

    /// <summary>
    /// The posterior match probability a pair's log-likelihood ratio implies once the prior is
    /// folded in -- the same quantity <see cref="LinkageConfidenceAdjuster"/> folds into a claim's
    /// confidence. Exposed because the clerical review protocol forms its strata from this band
    /// rather than from <see cref="LinkageClassification"/>, and two places computing it from the
    /// ratio by hand would eventually disagree about what the gray zone is.
    /// </summary>
    public static double PosteriorMatchProbability(PairLinkage pair, FieldLinkageParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(pair);
        ArgumentNullException.ThrowIfNull(parameters);

        return Posterior(pair.LogLikelihoodRatio, PriorLogOdds(parameters));
    }

    /// <summary>Whether a pair sits in the band where the fitted mixture does not really know which component it belongs to.</summary>
    public static bool IsAmbiguous(PairLinkage pair, FieldLinkageParameters parameters) =>
        PosteriorMatchProbability(pair, parameters) is > AmbiguousPosteriorLow and < AmbiguousPosteriorHigh;

    private static double PriorLogOdds(FieldLinkageParameters parameters) =>
        Math.Log(parameters.MatchPrior / (1.0 - parameters.MatchPrior));

    private static double Posterior(double logLikelihoodRatio, double priorLogOdds) =>
        1.0 / (1.0 + Math.Exp(-(logLikelihoodRatio + priorLogOdds)));

    public static LinkageErrorRateEstimate Estimate(IReadOnlyList<PairLinkage> pairs, FieldLinkageParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        ArgumentNullException.ThrowIfNull(parameters);

        var priorLogOdds = PriorLogOdds(parameters);

        double expectedMatches = 0, expectedNonMatches = 0, falseMatches = 0, falseNonMatches = 0;
        var grayZone = 0;
        var ambiguous = 0;
        foreach (var pair in pairs)
        {
            var posterior = Posterior(pair.LogLikelihoodRatio, priorLogOdds);
            expectedMatches += posterior;
            expectedNonMatches += 1.0 - posterior;

            switch (pair.Classification)
            {
                case LinkageClassification.Match:
                    falseMatches += 1.0 - posterior;
                    break;
                case LinkageClassification.NonMatch:
                    falseNonMatches += posterior;
                    break;
                case LinkageClassification.GrayZone:
                    grayZone++;
                    break;
            }

            if (posterior is > AmbiguousPosteriorLow and < AmbiguousPosteriorHigh)
            {
                ambiguous++;
            }
        }

        var caveats = LinkageErrorRateCaveat.None;
        if (parameters.Status == EstimationStatus.HeuristicDefault)
        {
            caveats |= LinkageErrorRateCaveat.HeuristicParameters;
        }

        if (parameters.Status == EstimationStatus.NotConverged)
        {
            caveats |= LinkageErrorRateCaveat.NotConverged;
        }

        if (pairs.Count < FellegiSunterEstimator.MinimumPairsForEmEstimation)
        {
            caveats |= LinkageErrorRateCaveat.TooFewPairs;
        }

        if (parameters.LabelsSwapped)
        {
            caveats |= LinkageErrorRateCaveat.LabelsSwapped;
        }

        if (pairs.Count > 0 && (double)ambiguous / pairs.Count >= MaxAmbiguousShare)
        {
            caveats |= LinkageErrorRateCaveat.PoorSeparation;
        }

        if (parameters.MAgreeProbability.Count < FellegiSunterEstimator.MinimumFieldsForIdentification)
        {
            caveats |= LinkageErrorRateCaveat.TooFewFields;
        }

        return new LinkageErrorRateEstimate(
            FalseMatchRate: expectedNonMatches <= 0.0 ? 0.0 : falseMatches / expectedNonMatches,
            FalseNonMatchRate: expectedMatches <= 0.0 ? 0.0 : falseNonMatches / expectedMatches,
            GrayZoneShare: pairs.Count == 0 ? 0.0 : (double)grayZone / pairs.Count,
            ExpectedMatches: expectedMatches,
            ExpectedNonMatches: expectedNonMatches,
            Caveats: caveats);
    }
}
