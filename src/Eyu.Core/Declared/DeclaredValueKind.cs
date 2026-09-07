namespace Eyu.Core.Declared;

/// <summary>
/// The kind of value a declared field holds, in Eyu's own vocabulary. A caller's schema system
/// has its own type names; the adapter that bridges it maps them here so that no consumer's type
/// system leaks into the judgment contract, and so that a model is told "integer" rather than left
/// to infer it from a sample that happens to contain digits.
/// </summary>
public enum DeclaredValueKind
{
    /// <summary>Free text.</summary>
    Text,

    /// <summary>A whole number.</summary>
    WholeNumber,

    /// <summary>A number with a fractional part (amounts, measurements).</summary>
    FractionalNumber,

    /// <summary>True or false.</summary>
    Boolean,

    /// <summary>A point in time.</summary>
    Timestamp,

    /// <summary>An opaque identifier (a UUID, a key) — carries identity, not meaning.</summary>
    Identifier,

    /// <summary>A nested structure the caller stores whole (a document, a JSON value).</summary>
    Structured,
}
