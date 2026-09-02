namespace Eyu.Core.Grounding;

/// <summary>
/// Points at the specific record (and, optionally, field within it) a grounded claim cites —
/// the "sources[]" element of <see cref="IGroundingContract"/>. <paramref name="RecordId"/>
/// matches <see cref="Eyu.Core.Records.RawRecord.Id"/>.
/// </summary>
public sealed record SourceRef(string RecordId, string? FieldName = null);
