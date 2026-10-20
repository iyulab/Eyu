namespace Eyu.Core.Records;

/// <summary>
/// A type name earlier work already uses, handed back so a new call names the same kind of thing the
/// same way. Eyu keeps nothing between calls: a caller that stores proposals holds the type names they
/// were exported under and passes them in, and an entity of a kind one of them names is proposed under
/// it rather than under a new name for the same kind (<c>part</c> stays <c>part</c>, not <c>item</c>).
/// Unlike a declared type, it is not a fact the caller asserts — it stays inferred vocabulary, and a
/// kind no known type names still gets a name of the model's own.
/// <para>
/// A known entity's type is already a known type; this record carries the kinds no known entity stands
/// for — those an earlier call proposed but the caller did not keep as an identity, or only mentioned.
/// </para>
/// </summary>
/// <param name="Name">The type name, as it is already in use.</param>
/// <param name="Specimens">
/// Records of the kind the name stands for — what a new record's kind is compared against, as a type
/// specimen anchors a species name. They are for comparison only: a claim in the new proposal can cite
/// only the records of its own call. May be empty when the name alone says what it means.
/// </param>
public sealed record KnownType(string Name, IReadOnlyList<RawRecord> Specimens)
{
    /// <summary>A known type with no specimen.</summary>
    public KnownType(string name)
        : this(name, [])
    {
    }

    /// <summary>
    /// Refuses known types that cannot mean one thing: a name with no letter or digit, one name written twice (names are
    /// compared ignoring case and separators, the way <see cref="TypeNames.SameType"/> reads a proposal),
    /// or a specimen that is also one of the call's own records — a record cannot be both what is already
    /// known and what is new.
    /// </summary>
    internal static void Validate(IReadOnlyList<KnownType> knownTypes, IReadOnlyList<RawRecord> records)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var specimenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var known in knownTypes)
        {
            ArgumentNullException.ThrowIfNull(known);
            if (string.IsNullOrWhiteSpace(known.Name) || TypeNames.Key(known.Name).Length == 0)
            {
                throw new ArgumentException("A known type needs a name with at least one letter or digit.", nameof(knownTypes));
            }

            if (!names.TryAdd(TypeNames.Key(known.Name), known.Name))
            {
                throw new ArgumentException(
                    $"The known types '{names[TypeNames.Key(known.Name)]}' and '{known.Name}' are one name written twice — a kind has one name in use.",
                    nameof(knownTypes));
            }

            foreach (var record in known.Specimens ?? [])
            {
                specimenIds.Add(record.Id);
            }
        }

        var overlap = records.Select(r => r.Id).Where(specimenIds.Contains).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (overlap.Count > 0)
        {
            throw new ArgumentException(
                $"The record(s) {string.Join(", ", overlap)} are both among this call's records and a known type's specimens — a record is either new or already known.",
                nameof(knownTypes));
        }
    }
}
