namespace Eyu.Core.Linkage;

/// <summary>
/// Where a record pair's Fellegi-Sunter log-likelihood ratio falls relative to the two
/// configured thresholds — see <see cref="LinkageClassifier.Classify"/>.
/// </summary>
public enum LinkageClassification
{
    NonMatch,
    GrayZone,
    Match,
}
