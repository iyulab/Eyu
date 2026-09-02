namespace Eyu.Core.Grounding;

/// <summary>
/// The shape every Eyu answer is expressed in — <c>{claim, sources[], path[]}</c> (see design
/// rationale §B). A claim that cannot cite at least one source cannot be constructed at all;
/// implementations enforce this at the type level, not by convention. <see cref="Path"/> records
/// the reasoning steps (e.g. "extract", "sum") that led from the sources to the claim.
/// </summary>
public interface IGroundingContract
{
    string Claim { get; }
    IReadOnlyList<SourceRef> Sources { get; }
    IReadOnlyList<string> Path { get; }
}
