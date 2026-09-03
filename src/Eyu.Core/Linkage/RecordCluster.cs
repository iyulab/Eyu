namespace Eyu.Core.Linkage;

/// <summary>A group of record ids Fellegi-Sunter confirmed denote the same entity (linked only by <see cref="LinkageClassification.Match"/> edges).</summary>
public sealed record RecordCluster(IReadOnlyList<string> RecordIds);
