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

    /// <summary>
    /// Also write, for each acquired class and property, the IRI the rule before the current one
    /// (<see cref="EyuVocabulary.IriRule"/>) minted for each spelling this proposal uses — marked
    /// <c>owl:deprecated</c> and pointing at the current IRI with <c>dcterms:isReplacedBy</c>, the way
    /// retired terms are kept in published ontologies. A store that merged an older export into its
    /// default graph has no other way to tell the retired term from the current one: the rule marker
    /// names the rule of the ontology node, not of each triple. Before 0.5.0 an acquired term was minted
    /// from the spelling a proposal used (<c>:Work%20Order</c>); now it is minted from the name as Eyu
    /// compares it (<c>:Workorder</c>). Only the previous rule is bridged, and only from the spellings in
    /// this proposal — another export may have spelled a term differently. Off by default: the default
    /// output is unchanged.
    /// </summary>
    public bool BridgeRetiredIris { get; init; }
}
