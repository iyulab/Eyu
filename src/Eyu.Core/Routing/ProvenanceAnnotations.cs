namespace Eyu.Core.Routing;

/// <summary>
/// Keys Eyu writes into <see cref="HoneAI.PredictionProvenance.Annotations"/> when it traces a
/// proposal. HoneAI's stamp is domain-neutral by design ("the adapter is the domain boundary"), so
/// everything Eyu-specific about a proposal — its route, origin, basis, cited records — travels here
/// rather than as fields the middleware would have to know about.
/// </summary>
public static class ProvenanceAnnotations
{
    /// <summary>The <see cref="ProposalRoute"/> the caller's policy assigned, by enum name.</summary>
    public const string Route = "eyu.route";

    /// <summary>The proposal's <see cref="Eyu.Core.Proposals.VocabularyOrigin"/>, by enum name.</summary>
    public const string Origin = "eyu.origin";

    /// <summary>The proposal's <see cref="Eyu.Core.Proposals.ProposalBasis"/>, by enum name.</summary>
    public const string Basis = "eyu.basis";

    /// <summary>The record ids the claim cites, as a JSON array of strings in citation order — ids are caller strings and may contain any delimiter.</summary>
    public const string Sources = "eyu.sources";
}
