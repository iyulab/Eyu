namespace Eyu.Rdf;

/// <summary>
/// Where the exported ontology's own terms live. There is no default: an ontology's namespace says who
/// is making its claims, and Eyu does not know who the caller is.
/// </summary>
public sealed record RdfExportOptions
{
    /// <summary>
    /// The namespace the caller's acquired classes and properties are minted under, and the stem of the
    /// ontology's own IRI. Absolute, and ending in <c>#</c> or <c>/</c> so a term appended to it stays
    /// inside it.
    /// </summary>
    public Uri BaseIri { get; }

    /// <param name="baseIri">See <see cref="BaseIri"/>.</param>
    public RdfExportOptions(Uri baseIri)
    {
        ArgumentNullException.ThrowIfNull(baseIri);
        if (!baseIri.IsAbsoluteUri)
        {
            throw new ArgumentException("The base IRI must be absolute.", nameof(baseIri));
        }

        var text = baseIri.AbsoluteUri;
        if (!text.EndsWith('#') && !text.EndsWith('/'))
        {
            throw new ArgumentException(
                "The base IRI must end in '#' or '/', so a term appended to it stays inside the namespace.",
                nameof(baseIri));
        }

        BaseIri = baseIri;
    }
}
