using System.Text.RegularExpressions;
using Eyu.Core.Declared;
using Eyu.Core.Proposals;
using Eyu.Core.Records;

namespace Eyu.Core.Judgment;

/// <summary>
/// Withdraws the identity claims an answer makes through a field the caller declared as a reference.
/// <see cref="DeclaredRelation.ViaField"/> says the field holds the key of the relation's other end —
/// whichever side carries it — so a record holding an entity's name there refers to that entity: a work
/// order naming its machine is not a record of the machine. Models read such a row as denoting both the
/// order and the machine, and a prompt alone did not stop it; this is the deterministic half.
/// <para>
/// Only declared fields are read: without a declaration nothing says which field is a reference, and
/// guessing would demote a record's own name. The entity's name must equal the field's value (case and
/// whitespace aside) — whether a differently written name is the same thing is a question for
/// known-entity matching, not for this check. And a record is never left denoting nothing: when every
/// entity it would denote is one it names in a reference field, the field is taken to also name the
/// record's own thing (an event titled by its cause) and the claims stand.
/// </para>
/// </summary>
internal static partial class DeclaredReferenceDenotation
{
    /// <summary>An entity of the answer that survived its own checks, with the records it claims denote it.</summary>
    internal sealed record Claim(string EntityId, string Name, IReadOnlyList<string> DenotedBy);

    /// <summary>The demotions <paramref name="claims"/> call for, in claim order then record order.</summary>
    public static IReadOnlyList<DemotedDenotation> Find(
        IReadOnlyList<DeclaredStructure> declaredStructures,
        IReadOnlyList<RawRecord> records,
        IReadOnlyList<Claim> claims)
    {
        var referenceFields = declaredStructures
            .SelectMany(s => s.Relations)
            .Select(r => r.ViaField)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (referenceFields.Count == 0)
        {
            return [];
        }

        var recordsById = records.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var candidates = new List<DemotedDenotation>();
        foreach (var claim in claims)
        {
            var name = Normalize(claim.Name);
            foreach (var recordId in claim.DenotedBy)
            {
                var field = referenceFields.FirstOrDefault(f =>
                    recordsById[recordId].Fields.TryGetValue(f, out var value) && value is not null && Normalize(value) == name);
                if (field is not null)
                {
                    candidates.Add(new DemotedDenotation(claim.EntityId, recordId, field));
                }
            }
        }

        // A record keeps every claim on it unless some entity it denotes is left after the demotions.
        var denoters = claims
            .SelectMany(c => c.DenotedBy.Select(r => (Record: r, c.EntityId)))
            .GroupBy(x => x.Record, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(x => x.EntityId).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        var demotedOn = candidates
            .GroupBy(d => d.RecordId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(d => d.EntityId).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        return candidates.Where(d => denoters[d.RecordId].Except(demotedOn[d.RecordId]).Any()).ToList();
    }

    private static string Normalize(string text) => Whitespace().Replace(text.Trim(), " ").ToUpperInvariant();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
