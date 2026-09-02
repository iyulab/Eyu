namespace Eyu.Core.Grounding;

/// <summary>Default vehicle for <see cref="IGroundingContract"/>; construct only via <see cref="Create"/>.</summary>
public sealed record GroundedClaim : IGroundingContract
{
    public string Claim { get; }
    public IReadOnlyList<SourceRef> Sources { get; }
    public IReadOnlyList<string> Path { get; }

    private GroundedClaim(string claim, IReadOnlyList<SourceRef> sources, IReadOnlyList<string> path)
    {
        Claim = claim;
        Sources = sources;
        Path = path;
    }

    /// <summary>
    /// Creates a <see cref="GroundedClaim"/>, rejecting a blank claim or one with no sources — "a
    /// claim that can't cite its sources cannot be expressed" (README), enforced here rather than
    /// left to caller discipline.
    /// </summary>
    public static GroundedClaim Create(string claim, IReadOnlyList<SourceRef> sources, IReadOnlyList<string>? path = null)
    {
        if (string.IsNullOrWhiteSpace(claim))
        {
            throw new ArgumentException("A claim must be a non-empty statement.", nameof(claim));
        }

        if (sources is null || sources.Count == 0)
        {
            throw new ArgumentException("A claim without sources cannot be expressed.", nameof(sources));
        }

        return new GroundedClaim(claim, sources, path ?? []);
    }
}
