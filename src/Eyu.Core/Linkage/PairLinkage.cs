namespace Eyu.Core.Linkage;

/// <summary>One record pair's Fellegi-Sunter classification and the log-likelihood ratio behind it.</summary>
public sealed record PairLinkage(string RecordIdA, string RecordIdB, LinkageClassification Classification, double LogLikelihoodRatio);
