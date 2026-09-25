namespace Eyu.Rdf;

/// <summary>
/// The terms Eyu itself defines in an exported ontology, under one namespace it owns. Two kinds live
/// here: the annotation properties that carry what a proposal knows about itself (its claim, the
/// records it cites, its confidence, whether its type was declared), and the classes and properties
/// of the innate vocabulary, which are Eyu's own domain-independent words rather than the caller's.
/// <para>
/// Nothing here is aligned to an outside vocabulary — innate <c>Person</c> is written as this
/// namespace's <c>Person</c>, not as another vocabulary's term of the same spelling. Deciding that two
/// vocabularies mean the same thing is the term normalization Eyu does not perform; a caller who wants
/// that alignment states it in their own graph, where it is their claim.
/// </para>
/// </summary>
public static class EyuVocabulary
{
    /// <summary>The namespace every Eyu-defined term is minted under.</summary>
    public const string Namespace = "https://github.com/iyulab/Eyu/vocab#";

    /// <summary>The statement a proposal makes, in the model's own words.</summary>
    public const string Claim = Namespace + "claim";

    /// <summary>One record a claim cites; its object carries <see cref="RecordId"/> and optionally <see cref="FieldName"/>.</summary>
    public const string Cites = Namespace + "cites";

    /// <summary>The cited record's id, as the caller's records name it.</summary>
    public const string RecordId = Namespace + "recordId";

    /// <summary>The field within the cited record, when the citation names one.</summary>
    public const string FieldName = Namespace + "fieldName";

    /// <summary>
    /// The id of a cited record that is a record of this individual itself, not one that only
    /// mentions it; two on one individual are the proposal's claim that those records are the same
    /// entity. Written on individuals only — a relation has no identity to claim.
    /// </summary>
    public const string DenotedBy = Namespace + "denotedBy";

    /// <summary>The proposer's confidence — a routing signal, uncalibrated unless the proposer says otherwise.</summary>
    public const string Confidence = Namespace + "confidence";

    /// <summary><c>"declared"</c> when the caller's declared structure names the type, <c>"inferred"</c> otherwise.</summary>
    public const string Basis = Namespace + "basis";

    /// <summary><c>"innate"</c> or <c>"acquired"</c> — which vocabulary a class or property was drawn from.</summary>
    public const string Origin = Namespace + "origin";

    /// <summary>
    /// On the ontology: the rule its class, property and individual IRIs were minted under, named by
    /// the Eyu release that introduced it (<c>"0.5.0"</c>). Individual IRIs are stable across a
    /// change of rule and term IRIs are not, so a store holding exports made under two rules types
    /// one individual into both the old term and the new; the triples an older rule wrote are the
    /// ones to retire. An export with no value was written by 0.5.0 or earlier, before the
    /// annotation existed. The value changes only when a minting rule does.
    /// </summary>
    public const string IriRule = Namespace + "iriRule";
}
