using Eyu.Core.Linkage;

namespace Eyu.Core.Tests.Quality;

/// <summary>
/// How the cross-source measurement joins a proposed individual to the case's things, kept outside
/// the live instrument so it can be held to expectations without a model.
/// <para>
/// A name is compared with case and separators ignored — the join a consumer merging exports by label
/// would make. An individual whose name is none of a thing's names can still be one that stands for
/// several: a model reading every source at once wrote <c>PRS-004 / CNV-012 (프레스 4호기 / 컨베이어 12)</c>
/// for two machines. <see cref="OverMerged"/> names that case, so a measurement can tell a thing the
/// model called something else from two things it made one.
/// </para>
/// </summary>
internal static class CrossSourceJoin
{
    /// <summary>The things one of whose names is exactly this name, compared leniently.</summary>
    public static IReadOnlyList<SameThing> Match(CrossSourceCase crossSource, string name)
    {
        var key = Lenient(name);
        return crossSource.Things.Where(t => t.Names.Any(n => Lenient(n) == key)).ToList();
    }

    /// <summary>
    /// The distinct things this name carries a name of, when it carries names of two or more — an
    /// individual standing for several things. Empty otherwise.
    /// </summary>
    public static IReadOnlyList<SameThing> OverMerged(CrossSourceCase crossSource, string name)
    {
        var key = Lenient(name);
        var carried = crossSource.Things.Where(t => t.Names.Any(n => key.Contains(Lenient(n), StringComparison.Ordinal))).ToList();
        return carried.Count >= 2 ? carried : [];
    }

    /// <summary>
    /// How many of the entities whose denoting records are <paramref name="denotations"/> are denoted by
    /// records of two or more parents — rows of
    /// different sheets made one entity. Ownership is read from the case's declarations
    /// (<see cref="LinkageEvidence.OwnershipScope"/>), so it counts what the name join cannot see: rows
    /// have no names of their own, and the same item checked on two sheets carries one name twice.
    /// </summary>
    public static int DenotedAcrossParents(CrossSourceCase crossSource, IEnumerable<IReadOnlyList<string>> denotations)
    {
        var records = crossSource.Sources.SelectMany(s => s.Records).ToList();
        var scope = LinkageEvidence.OwnershipScope(records, [.. crossSource.Sources.SelectMany(s => s.Declarations)]);
        var byId = records.GroupBy(r => r.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return denotations.Count(denotedBy => denotedBy
            .Select(id => byId.TryGetValue(id, out var record) ? scope(record) : null)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Count() >= 2);
    }

    private static string Lenient(string name) => new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
