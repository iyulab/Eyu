namespace Eyu.Core.Linkage;

/// <summary>
/// Routes a pair's log-likelihood ratio into <see cref="LinkageClassification.Match"/>,
/// <see cref="LinkageClassification.GrayZone"/>, or <see cref="LinkageClassification.NonMatch"/>.
/// Only <see cref="LinkageClassification.GrayZone"/> pairs are put to the model for adjudication;
/// Match and NonMatch are decided here by Fellegi-Sunter alone (design decision 1). What that
/// decision does <em>not</em> fix is the confidence a claim over such a pair ends up with: a
/// claim citing a confirmed Match cluster gets the prior alone, while one touching a GrayZone or
/// NonMatch pair folds the model's number in — see <see cref="LinkageConfidenceAdjuster"/>.
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
