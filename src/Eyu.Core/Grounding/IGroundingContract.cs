namespace Eyu.Core.Grounding;

/// <summary>
/// The shape every Eyu answer is expressed in — <c>{claim, sources[], path[]}</c> (see design
/// rationale §B). A claim that cannot cite at least one source cannot be constructed at all;
/// implementations enforce this at the type level, not by convention. <see cref="Path"/> is an
/// optional slot for the reasoning steps (e.g. "extract", "sum") that led from the sources to the
/// claim — filled by whoever constructs the claim, and Eyu's own proposer
/// (<see cref="Eyu.Core.Judgment.SinglePassOntologyProposer"/>) does not fill it: every claim it
/// returns carries an empty path. A consumer that wants a reasoning trace supplies one itself.
/// </summary>
public interface IGroundingContract
{
    string Claim { get; }
    IReadOnlyList<SourceRef> Sources { get; }
    IReadOnlyList<string> Path { get; }
}
