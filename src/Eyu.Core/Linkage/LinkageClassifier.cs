namespace Eyu.Core.Linkage;

/// <summary>
/// Routes a pair's log-likelihood ratio into <see cref="LinkageClassification.Match"/>,
/// <see cref="LinkageClassification.GrayZone"/>, or <see cref="LinkageClassification.NonMatch"/>.
/// Only <see cref="LinkageClassification.GrayZone"/> pairs need the LLM's judgment — Match and
/// NonMatch are decided by Fellegi-Sunter alone (design decision 1).
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
