namespace Eyu.Core.Linkage;

/// <summary>
/// Routes a pair's log-likelihood ratio into <see cref="LinkageClassification.Match"/>,
/// <see cref="LinkageClassification.GrayZone"/>, or <see cref="LinkageClassification.NonMatch"/>.
/// Only <see cref="LinkageClassification.GrayZone"/> pairs are put to the model for adjudication;
/// Match and NonMatch are decided here by Fellegi-Sunter alone (design decision 1). What that
/// decision does <em>not</em> fix is the confidence a claim over such a pair ends up with: a
/// claim citing a confirmed Match cluster gets the prior alone, while one touching a GrayZone or
/// NonMatch pair folds the model's number in — see <see cref="LinkageConfidenceAdjuster"/>.
/// <para>
/// The thresholds are on the <em>prior-free</em> scale: the log-likelihood ratio alone, in nats of
/// field evidence — the classic Fellegi–Sunter test — not the posterior log-odds. A caller who
/// thinks in probabilities converts with <c>posterior = 1 / (1 + exp(-(ratio + logit(MatchPrior))))</c>
/// (<see cref="FieldLinkageParameters.MatchPrior"/>). Everything downstream that reports a
/// probability — a claim's confidence, <see cref="LinkageErrorRateEstimate"/>, the clerical review
/// strata — uses that posterior, so on a large batch, where the fitted prior is small, a pair just
/// past the match threshold can be a Match whose posterior is low. The split is deliberate: the
/// classification weighs the field evidence only, not the fitted prior, which falls as unrelated
/// records are added to the batch. Measured on a labeled benchmark (<c>docs/linkage-benchmark.md</c>), reading the same
/// thresholds on the posterior scale handed the model 4 to 40 times fewer gray-zone pairs but made
/// more errors no adjudication can undo (57 against 23 over eight configurations).
/// </para>
/// </summary>
public static class LinkageClassifier
{
    public const double DefaultMatchThreshold = 4.0;
    public const double DefaultNonMatchThreshold = -4.0;

    public static LinkageClassification Classify(
        double logLikelihoodRatio,
        double matchThreshold = DefaultMatchThreshold,
        double nonMatchThreshold = DefaultNonMatchThreshold)
    {
        if (matchThreshold <= nonMatchThreshold)
        {
            throw new ArgumentException(
                $"{nameof(matchThreshold)} ({matchThreshold}) must be greater than {nameof(nonMatchThreshold)} ({nonMatchThreshold}).",
                nameof(matchThreshold));
        }

        if (logLikelihoodRatio >= matchThreshold)
        {
            return LinkageClassification.Match;
        }

        return logLikelihoodRatio <= nonMatchThreshold ? LinkageClassification.NonMatch : LinkageClassification.GrayZone;
    }
}
