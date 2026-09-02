namespace Eyu.Core.Primitives;

/// <summary>
/// Identifies the kind of thing declared structure or sampled records pertain to (e.g. "invoice",
/// "employee") — the unit <see cref="Eyu.Core.Ports.IStructureSource"/> and
/// <see cref="Eyu.Core.Ports.IRecordSample"/> are keyed on. Eyu-owned and consumer-agnostic: it
/// never wraps a source system's own type identifier (e.g. formbase's FormTypeRef) so that
/// non-formbase consumers are never coupled to a formbase concept.
/// </summary>
public readonly record struct SubjectRef
{
    /// <summary>The normalized (trimmed) subject identifier.</summary>
    public string Value { get; }

    private SubjectRef(string value) => Value = value;

    /// <summary>Creates a validated <see cref="SubjectRef"/>, rejecting null/blank input.</summary>
    public static SubjectRef Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Subject must be a non-empty identifier.", nameof(value));
        }

        return new SubjectRef(value.Trim());
    }

    public override string ToString() => Value;
}
