namespace Eyu.Core.Linkage;

/// <summary>
/// What record linkage says about one of a call's records and one known entity: the strongest evidence
/// that the record denotes that entity, taken over the known entity's records. Only
/// <see cref="LinkageClassification.Match"/> and <see cref="LinkageClassification.GrayZone"/> are kept —
/// a non-match is the default and says nothing a caller acts on.
/// </summary>
/// <param name="RecordId">The call's record.</param>
/// <param name="KnownEntityKey">The known entity's key (<see cref="Records.KnownEntity.Key"/>).</param>
/// <param name="Classification">Match (the pre-filter decided) or GrayZone (left to judgment).</param>
/// <param name="LogLikelihoodRatio">The prior-free log-likelihood ratio of the best-matching known record.</param>
public sealed record KnownEntityCandidate(string RecordId, string KnownEntityKey, LinkageClassification Classification, double LogLikelihoodRatio);
